---

description: "Task list for Phase 5 authenticated password change"
---

# Tasks: Phase 5 — Authenticated Password Change

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md), and
[authentication-api-password.openapi.yaml](contracts/authentication-api-password.openapi.yaml)

**Prerequisites**: Approved Phase 5 plan and clarified specification; Gate G4 closed (recorded in
the roadmap).

**Tests**: Required. Use the existing xUnit v3 projects, `WebApplicationFactory`, real Identity and
SQLite, and `ControlledTimeProvider`; no sleeps, EF InMemory, new test projects, new packages, or
unnecessary mocks. Scenarios are consolidated per [quickstart.md](quickstart.md) §2; do not add one
test per requirement.

**Organization**: Tasks are grouped by user story. The shared enum value, application port, and
registration are completed first. The adapter and endpoint are built incrementally: US1 delivers
the atomic password change, US2 adds session revocation to the same transaction, US3 proves the
administrator flow with no administrator-specific logic.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites because it affects a distinct file.
- **[US#]**: The user story served by a task. Shared setup and foundation work has no story label.

## Phase 1: Setup and Test Support

**Purpose**: Make the Phase 5 scenarios expressible on the existing test host without new
infrastructure.

- [X] T001 Extend reusable test configuration in `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs` only as the Phase 5 scenarios need: an override of the externally configured Identity password policy (for example `Identity__Password__RequiredLength`) and capture of the host's log output for secret scanning; reuse the existing cookie/origin helpers and add no package.

---

## Phase 2: Foundational Contracts

**Purpose**: Add the revocation reason and the application contract that all stories use.

**⚠️ CRITICAL**: Complete this phase before implementing any user story.

- [X] T002 Append `PasswordChanged = 4` to `SessionRevocationReason` in `src/Authentication.Domain/Sessions/SessionRevocationReason.cs`, keeping `Replay = 0`, `Logout = 1`, `UserDisabled = 2`, `Administrator = 3` unchanged; no other Domain change.
- [X] T003 [P] Add the `Passwords` application slice under `src/Authentication.Application/Features/Passwords/`, one top-level type per matching file: `IPasswordChange.cs` (`Task<ChangePasswordOutcome> ChangeAsync(ChangePasswordCommand, CancellationToken)`), `ChangePasswordCommand.cs` (`UserId`, `CurrentPassword`, `NewPassword`, optional `PresentedRefreshTokenHash` as `byte[]?`), and `ChangePasswordOutcome.cs` (`Changed`, `InvalidCurrentPassword`, `InvalidNewPassword`, `Invalid`); no Identity, HTTP, or EF types.
- [X] T004 Verify Phase 5 needs no schema change by running `dotnet ef migrations has-pending-model-changes --project src/Authentication.Infrastructure --startup-project src/Authentication.Api` after T002 and confirming it reports none; do not add a migration.

**Checkpoint**: The Domain/Application contract exists, and the model proves no persistence change is required.

---

## Phase 3: User Story 1 - Authenticated Password Change (Priority: P1) 🎯 MVP

**Goal**: An authenticated user of any role replaces their own password with the current and a valid
new one; the old password stops authenticating and the new one works, with no other account data
changed and every failure leaving the credential untouched.

**Independent Test**: With a valid bearer token, `POST /api/auth/change-password` returns `204`,
the old password logs in `401` and the new one `200`; unauthenticated, incorrect-current, invalid-body,
and policy-violating requests change nothing.

### Tests for User Story 1

- [X] T005 [P] [US1] Add consolidated change-password integration scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordChangeTests.cs` for: missing/invalid/expired bearer token → `401` with `WWW-Authenticate: Bearer` and no change; non-JSON, malformed JSON, and missing or blank field → `400 The request is invalid.`; a valid body that also carries another user's `userId` and `email` changes only the caller's password while the other user's password still logs in; incorrect current password → `401 Invalid credentials.` without `WWW-Authenticate`, password and sessions unchanged and `AccessFailedCount` incremented by exactly one (SRS NFR-SEC-BF-001), and `MaxFailedAccessAttempts` consecutive failures (read from the host's effective `IdentityOptions`) locking login out; a new password violating a policy raised through T001 → `400` with the policy detail and no change; a valid change by a non-administrator user → `204` with no body or `Set-Cookie`, old password login `401`, new password login `200`, and email, roles, enabled state unchanged; a password write failure injected by a temporary SQLite `BEFORE UPDATE ON AspNetUsers` trigger using `RAISE(ABORT)` → generic `503`, after which the trigger is dropped and the old password still logs in; and no response body in these scenarios contains the submitted current or new password (quickstart scenarios 1–5, 13 and 15).
- [X] T006 [P] [US1] Add a deterministic concurrent-change scenario in `tests/Authentication.IntegrationTests/Scenarios/PasswordChangeConcurrencyTests.cs`: two requests for one user issued concurrently with `Task.WhenAll` (no sleeps) yield exactly one `204` and one `401` whether or not they overlap, and only the winning new password authenticates (quickstart scenario 12).

### Implementation for User Story 1

- [X] T007 [US1] Implement `src/Authentication.Infrastructure/Identity/PasswordChange.cs` for `IPasswordChange`: open an `IsolationLevel.Serializable` transaction on `AuthenticationDbContext`, load the user by `UserId` (unknown subject → `InvalidCurrentPassword`), call `UserManager.ChangePasswordAsync`, on `PasswordMismatch` call `UserManager.AccessFailedAsync(user)` when `SupportsUserLockout`, commit only that counter update, and return `InvalidCurrentPassword`; map `Password*` codes → `InvalidNewPassword` and other failures → `Invalid` without leaking Identity descriptions, returning without committing; commit on success. Add no lockout check, do not reset the failed-attempt counter on success, and touch no attribute other than the credential (research §3).
- [X] T008 [US1] Register `IPasswordChange` → `PasswordChange` as a scoped service in `src/Authentication.Infrastructure/DependencyInjection.cs`.
- [X] T009 [P] [US1] Add `ChangePasswordRequest` in `src/Authentication.Api/Features/Passwords/ChangePasswordRequest.cs` with `currentPassword` and `newPassword`, an `IsValid` check for non-blank values, and no account-identifier member, following the `LoginRequest` convention.
- [X] T010 [US1] Add `POST /api/auth/change-password` in `src/Authentication.Api/Features/Passwords/ChangePasswordEndpoint.cs`: `RequireAuthorization()` so the established JWT challenge answers `401`; read and validate the body as `LoginEndpoint` does (`JsonException`/invalid → `400 The request is invalid.`); `InitializationState.IsReady` else `503`; take the account only from the `sub` claim (absent → `401 Invalid credentials.`); map `Changed` → `204` (no body, no cookie), `InvalidCurrentPassword` → `401 Invalid credentials.` without `WWW-Authenticate`, `InvalidNewPassword` → `400 The password does not satisfy the password policy.`, `Invalid` → `400`, and `DbException`/`DbUpdateException` → `503 The service is not ready.`.
- [X] T011 [US1] Mount the endpoint with `MapChangePasswordEndpoint()` in `src/Authentication.Api/Program.cs` without changing the JWT, Origin, or CORS configuration.

**Checkpoint**: US1 is independently demonstrable without session revocation; consumer JWT validation is unchanged.

---

## Phase 4: User Story 2 - Other Sessions Stop Renewing (Priority: P1)

**Goal**: A successful change revokes, in the same transaction, every active renewable session of the
user except the family identified by a usable `auth_refresh` cookie of the same user; without such
a cookie all are revoked; failed changes revoke nothing; access tokens stay stateless.

**Independent Test**: With two families for one user, change the password presenting family A's
cookie: A still refreshes and B no longer does; a failed change leaves both usable.

### Tests for User Story 2

- [X] T012 [P] [US2] Add consolidated session-revocation scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordChangeSessionRevocationTests.cs`: with the cookie of family A the change keeps A refreshing (`200`) and makes B refresh `401`, with B persisted as revoked with reason `PasswordChanged` and A unmodified; with no, malformed, unknown, expired (via `ControlledTimeProvider`), already-revoked, or another user's cookie every family of the user is revoked while the other user's family is untouched; each failed change from T005's rejection cases leaves both families refreshing; a revocation write failure injected after the password update by a temporary `BEFORE UPDATE ON RenewableSessionFamilies` trigger using `RAISE(ABORT)` returns generic `503`, and after dropping the trigger the old password still logs in, the new one does not, and both families still refresh (FR-010); and one captured change event contains the user id, revoked count, UTC time, and trace identifier while no password, hash, security stamp, access token, or cookie value appears in captured logs (quickstart scenarios 6–8, 13b and 14).

### Implementation for User Story 2

- [X] T013 [US2] Extend `src/Authentication.Infrastructure/Identity/PasswordChange.cs` inside the existing transaction: resolve the kept family exactly per [data-model.md](data-model.md) (digest matches a credential that is not consumed, not revoked, not expired, whose family is active and owned by `UserId`; otherwise keep none), run `ChangePasswordAsync` as before, then load the user's non-revoked families and revoke every one that `IsActive(now)` and is not the kept family with `SessionRevocationReason.PasswordChanged` using `TimeProvider`, saving before commit so a failure rolls back both the credential and the revocations; never modify the kept family or any `RefreshCredential` row.
- [X] T014 [US2] Pass the optional refresh digest from the `auth_refresh` cookie in `src/Authentication.Api/Features/Passwords/ChangePasswordEndpoint.cs` using `RefreshCredentialProtector.TryHash` (a rejected value yields no digest); the cookie only selects the kept family, never authorizes the request, no `Origin` check is added, and no `Set-Cookie` is emitted.
- [X] T015 [US2] Add the post-commit structured security event in `src/Authentication.Infrastructure/Identity/PasswordChange.cs` using `LoggerMessage` at `Information` with user id, number of revoked families, whether a session was kept, UTC instant from `TimeProvider`, and `Activity` trace/span identifiers; log nothing on refusals and exclude passwords, hashes, security stamps, tokens, and cookie values.

**Checkpoint**: US2 completes the atomic change-and-revoke behavior with no blacklist or consumer changes.

---

## Phase 5: User Story 3 - Initial Administrator Retires the Default Password (Priority: P1)

**Goal**: The built-in administrator replaces the initial `admin` password through the same
operation, without email or administrator-specific logic, and the replacement survives restarts.

**Independent Test**: Sign in as `admin@local.invalid` / `admin`, change the password, restart on
the same database file, and verify only the new password signs in.

### Tests for User Story 3

- [X] T016 [P] [US3] Add administrator scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordChangeAdministratorTests.cs`: the built-in administrator changes `admin` to a new valid password with `204` and no email dependency, the initial password then fails `401` and the new one succeeds, the account remains the only enabled Administrator with role and enabled state unchanged, and after recreating the factory on the same temporary SQLite file the new password still works and `admin` does not (quickstart scenarios 10–11).

### Implementation for User Story 3

- [X] T017 [US3] Create `docs/phase-5-operations.md` with the operator procedure to retire the initial `admin` password through `POST /api/auth/change-password` (SRS NFR-SEC-ADMIN-001), the request/response behavior and session-revocation rule from the contract, and the statement that bootstrap never restores `admin` after restart, rebuild, or redeploy; Gate G5 evidence is added in T022–T023.

**Checkpoint**: US3 shows the default credential can be retired using the same code path as any user. The existing bootstrap is expected to skip an existing administrator; if T016 fails, correct `src/Authentication.Infrastructure/Persistence/DatabaseInitializer.cs` within US3 before the checkpoint is reached.

---

## Phase 6: Gate G5 Verification and Cross-Cutting Evidence

**Purpose**: Demonstrate the completed vertical slice, local consumer validation, the absence of
email infrastructure, and all Phase 1–4 regression behavior.

- [X] T018 [P] Add a Phase 5 Compose acceptance lifecycle in `tests/acceptance/phase-5.sh`, modeled on `tests/acceptance/phase-4.sh` and exporting the same default `AUTH_FRONTEND_ORIGIN`: change the initial administrator password; prove a second administrator session can no longer refresh while the session whose cookie accompanied the change can; run `docker compose restart auth-api` and prove the new secret signs in and `admin` does not; prove an ordinary user can change their password; and finish by running `tests/acceptance/phase-4.sh` as regression (which runs Phases 3–1).
- [X] T019 Extend the log scan in `tests/acceptance/phase-5.sh` to assert the `auth-api` logs contain no old or new password, access token, refresh cookie value, or `PRIVATE KEY`, and that the password-change event is present with a UTC timestamp and trace identifier.
- [X] T020 [P] Add consumer evidence in `tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs` that APIs A and B still accept an unexpired access token issued before the password change, with no consumer-side change (quickstart scenario 9).
- [X] T021 Verify no premature email or recovery infrastructure by running `grep -rn -E 'IEmailSender|MailKit|SmtpClient|forgot|reset-password' src` and confirming it returns nothing, and confirm no new NuGet package or Compose service was added; record both results in `docs/phase-5-operations.md`.
- [X] T022 Run `dotnet build` and the complete unit/integration suite, resolve Phase 5 and Phase 1–4 regressions, and record the exact commands and PASS evidence in `docs/phase-5-operations.md`, keeping `specs/005-phase-5-authenticated-password-change/quickstart.md` as the reusable validation guide.
- [X] T023 Run `tests/acceptance/phase-5.sh` and record all five Gate G5 evidence states—build, tests, startup, feature, and regression—in `docs/phase-5-operations.md`, linked to the focused test and acceptance output.
- [ ] T024 After T001-T023, review implementation and evidence against Roadmap §11.6, present `docs/phase-5-operations.md` to the project owner, and obtain explicit Gate G5 approval. Only after that approval, update the authorized Phase 5 checklist/status/progress records in `baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md` and `specs/005-phase-5-authenticated-password-change/checklists/requirements.md`, record the approval date/evidence reference without changing normative requirements, and create the identifiable Gate G5 closing commit; never mark Phase 5 complete or create the closing commit before approval.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** has no implementation dependencies.
- **Phase 2**: T002 first; T003 may run in parallel with it; T004 depends on T002.
- **US1** depends on Phases 1–2 (T007 needs T002/T003; T010 needs T007, T008, T009).
- **US2** depends on US1 because it extends the same adapter and endpoint (T013–T015 touch the files of T007 and T010).
- **US3** depends on US1 and is independent of US2's revocation logic; T016 should run after US2 so the full behavior is exercised.
- **Gate G5 verification** depends on all story phases.

### User Story Completion Order

```text
Setup → Foundation → US1 → US2 → US3 → Gate G5
```

### Parallel Opportunities

- T002/T003, T005/T006, T009 with T007/T008, and T018/T020 can run in parallel once their
  prerequisites are satisfied. T007, T013, and T015 share `PasswordChange.cs`, and T010 and T014
  share `ChangePasswordEndpoint.cs`, so those tasks run sequentially.

## Parallel Example: User Story 1

```text
Task: "Add rejection and success scenarios in tests/Authentication.IntegrationTests/Scenarios/PasswordChangeTests.cs"
Task: "Add the concurrent-change scenario in tests/Authentication.IntegrationTests/Scenarios/PasswordChangeConcurrencyTests.cs"
Task: "Add ChangePasswordRequest in src/Authentication.Api/Features/Passwords/ChangePasswordRequest.cs"
```

## Implementation Strategy

### MVP First

1. Complete setup and the foundational contract.
2. Complete US1 and prove the atomic password change with every rejection leaving the credential
   unchanged.
3. Validate US1 independently before adding session revocation.

### Incremental Delivery

1. US1 → authenticated password change.
2. US2 → atomic revocation of the other sessions.
3. US3 → retirement of the default administrator credential, with restart proof.
4. Gate G5 → Compose lifecycle plus Phase 1–4 regressions.

## Notes

- Do not add a migration, package, Compose service, `Origin` check, lockout check, throttling,
  handler/repository layer, or administrator-specific password logic; failed current-password
  attempts are counted only through Identity's `AccessFailedAsync`; rate limiting stays in Phase 7.
- Keep passwords, hashes, security stamps, access tokens, and refresh cookie values out of logs,
  contracts, and assertions.
- Do not create `plan.md` changes, frontend work, a second database, forgot/reset endpoints, email
  infrastructure, session-management endpoints, or centralized JWT validation.
