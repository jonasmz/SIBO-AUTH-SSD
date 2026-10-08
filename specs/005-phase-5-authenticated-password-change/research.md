# Phase 0 Research: Authenticated Password Change

All planning questions are resolved below. There are no remaining `NEEDS CLARIFICATION` items.

## 1. Credential replacement mechanism

**Decision**: Use `UserManager<ApplicationUser>.ChangePasswordAsync(user, current, new)` unchanged.
It verifies the current password with the configured `IPasswordHasher`, runs the registered
password validators (the existing externally configurable `Identity` policy), stores the new hash,
and updates the security stamp through Identity's own `UpdateAsync`.

**Rationale**: SRS FR-CHANGE-PWD-002/003 and Roadmap §11.2 require verification and update through
Identity; Technical Constraints §8 and Constitution V prohibit custom hashing, verification or
security-stamp behavior. One Identity call covers all three rules and returns stable error codes
(`PasswordMismatch`, `Password*`) that the adapter maps to fixed outcomes. No other user attribute
is touched (spec FR-006, US1-AS5).

**Alternatives considered**: `CheckPasswordAsync` + `RemovePasswordAsync` + `AddPasswordAsync`
was rejected: two writes, a window with no password, and more code for the same result. Calling
`IPasswordHasher` directly was rejected as manual password handling.

## 2. Status codes and error contract

**Decision**:

| Condition | Response |
|---|---|
| Missing/invalid/expired access token | `401`, existing JWT challenge ProblemDetails (`Authentication is required.`, `WWW-Authenticate: Bearer`) |
| Not JSON, malformed JSON, missing/blank `currentPassword` or `newPassword` | `400`, `The request is invalid.` |
| Incorrect current password | `401`, `Invalid credentials.` (no `WWW-Authenticate` header) |
| Token subject no longer resolves to a user | `401`, `Invalid credentials.` |
| New password violates the Identity policy | `400`, `The password does not satisfy the password policy.` |
| Not ready / `DbException` / `DbUpdateException` | `503`, `The service is not ready.` |
| Success | `204 No Content`, no body, no cookie change |

**Rationale**: The SRS error table (§34: *Credencial inválida → 401*, *Request inválido → 400*,
*Servicio temporalmente no disponible → 503*) settles the codes by precedence; the details reuse
the exact strings already returned by login, user administration and the JWT challenge. The
current-password `401` omits `WWW-Authenticate` and carries a different `detail`, so a client can
distinguish it from an expired access token. Identity error descriptions never leave the adapter
(FR-014, SRS FR-USER-005).

**Alternatives considered**: `400` for an incorrect current password (common in Identity samples)
was rejected because it contradicts the SRS mapping of an invalid credential to `401`. `403` was
rejected because the caller is authenticated and authorized for the operation. `422` is not part of
the SRS convention.

## 3. Failed-attempt counting and lockout

**Decision**: Phase 5 adds no failure counting, lockout check or throttling to change-password.
`ChangePasswordAsync` does not call `AccessFailedAsync`, and the adapter does not add it. A locked or
disabled user holding a still-valid access token is treated like any authenticated caller.

**Rationale**: The spec assumption and edge case limit the phase to the established behavior and
defer throttling to Phase 7; FR-LOGIN-005/006 concern login only. The attack surface is bounded by
the short access-token lifetime and the requirement to already hold a valid token.

**Alternatives considered**: Counting failures toward Identity lockout was rejected as an addition
the spec explicitly excludes; it would also let a holder of a stolen token lock the real user out.

## 4. Atomic change and session revocation

**Decision**: One `IsolationLevel.Serializable` transaction on the scoped `AuthenticationDbContext`
contains: loading the user by token subject, resolving the kept family, `ChangePasswordAsync`, and
revoking the remaining active families with reason `PasswordChanged`. Any refusal returns before
commit, and disposal rolls back. Success is returned only after commit.

**Rationale**: `UserManager` uses the same scoped context, so its writes join the transaction —
the pattern already used by `UserAdministration.SetEnabledAsync`. This makes FR-009 (failure revokes
nothing), FR-010 (no success while other sessions can renew) and the persistence edge case (failure
leaves password and sessions unchanged) hold by construction. SQLite serializable transactions
acquire the write lock up front, so two simultaneous changes for one account execute one after the
other: the second verifies against the already-replaced password and receives `401`, leaving one
coherent password and a session state that matches it. A concurrent refresh either commits before
(its new credential belongs to an existing family that is then revoked) or after (it sees the
revoked family and fails).

**Alternatives considered**: Changing the password first and calling `ISessionFamilyRevocation`
afterwards was rejected: a failure between the two would violate FR-010. Relying only on Identity's
`ConcurrencyStamp` was rejected because it does not cover the session rows.

## 5. Identifying the session to keep

**Decision**: The endpoint reads the `auth_refresh` cookie (already sent: its `Path=/api/auth`
covers `/api/auth/change-password`) and passes its SHA-256 digest only if
`RefreshCredentialProtector.TryHash` accepts it. Inside the transaction, the adapter keeps a family
only when the digest matches a credential that is not consumed and not revoked, has not expired,
and whose family is active and owned by the authenticated user. Otherwise nothing is kept. Every
other active family of the user is revoked; the kept family and its credential are not modified.

**Rationale**: Implements the clarified FR-008 and FR-017: the cookie only selects which family
survives, never authenticates, and presenting it changes nothing about that family. A consumed
credential is treated as unusable (it could not refresh either), so its family is revoked like any
other — no replay handling is triggered from this endpoint.

**Alternatives considered**: Revoking every family, including the current one, was rejected by the
clarification. A session-id claim in the access token was rejected by FR-017. Treating a consumed
credential as replay was rejected because it would make cookie presentation change state.

## 6. Origin validation

**Decision**: No `Origin` check on change-password.

**Rationale**: The request is authorized solely by the `Authorization: Bearer` header, which a
browser never attaches automatically, so it is not CSRF-reachable. The cookie confers no authority
(FR-017); the worst a forged cross-site request could do is nothing, because it lacks the bearer
token. Phase 4 Origin checks protect cookie-authorized endpoints (`refresh`, `logout`) and remain
unchanged.

**Alternatives considered**: Reusing `BrowserOriginValidator` was rejected: it would break
non-browser clients without closing any reachable attack.

## 7. Revocation reason persistence

**Decision**: Append `PasswordChanged` to `SessionRevocationReason`. No migration.

**Rationale**: The spec's key-entity note allows extending reasons only as needed. The column is
`int?` (model snapshot) with no conversion; appending keeps existing stored values (`0..3`) stable
and the check constraint only tests nullness. A model-diff check (`dotnet ef migrations
has-pending-model-changes`) verifies no schema change is required.

**Alternatives considered**: Reusing `Administrator` was rejected because it would misreport who
ended the sessions. A string conversion would require a migration for no current need.

## 8. Placement and types

**Decision**: New slice `Features/Passwords` in Application (port, command, outcome) and API
(endpoint, request). Infrastructure adapter `Identity/PasswordChange.cs`. No handler class: the
endpoint calls the port directly, as user administration does, because there is no orchestration
beyond the single transactional adapter call.

**Rationale**: Constitution III/IV: the only behavior lives in one Identity+EF transaction, which
must be in Infrastructure. A pass-through handler would add a type without isolation value.
Domain is unchanged except for the enum value; `RenewableSessionFamily.Revoke`/`IsActive` already
express the state transition.

**Alternatives considered**: Putting the operation on `IUserAdministration` was rejected: that
port serves `/api/admin/*` and is administrator-only by intent. Extending `RenewableSessionStore`
was rejected because the transaction is driven by the Identity write.

## 9. Security event logging

**Decision**: After commit, log one `Information` `LoggerMessage` event: user id, number of revoked
families, whether a session was kept, UTC instant from `TimeProvider`, `Activity` trace/span ids.
Refusals log nothing new beyond the existing request pipeline.

**Rationale**: SRS NFR-LOG-002 lists *cambio de contraseña* and *sesiones revocadas*; FR-015
requires UTC and correlation and forbids secrets. The template mirrors existing session events.
Integration tests scan captured logs for the submitted passwords and the cookie value.

**Alternatives considered**: Logging failed attempts was rejected as new monitoring scope not
required by the phase.

## 10. Verification approach

**Decision**: One integration test class per user story on the existing `AuthenticationApiFactory`
with real Identity, SQLite and `ControlledTimeProvider`; a temporary-file restart test for the
administrator; `tests/acceptance/phase-5.sh` against Compose (including `docker compose restart
auth-api`) plus reruns of `phase-1.sh`…`phase-4.sh`; Gate G5 evidence in
`docs/phase-5-operations.md`. A deterministic concurrency test starts two change requests for one
user with a barrier and asserts exactly one `204`. No new package, test project or mocking library.

**Rationale**: NFR-001/002, Constitution VI and Roadmap §11.5–11.6.

**Alternatives considered**: Unit-testing the adapter with a mocked `UserManager` was rejected:
Identity and SQLite behavior must be verified through integration.
