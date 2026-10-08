# Phase 3 Operations

Phase 3 adds administration of users and roles to Authentication API. There is no new service,
volume, or storage: the same `auth-api`, `api-a`, and `api-b` Compose services run as in Phase 2.

## Configuration

`auth-api` now validates the access tokens it issues, so it needs the same explicit clock
tolerance the consumers use:

| Variable | Purpose |
|---|---|
| `AUTH_JWT_CLOCK_SKEW_SECONDS` | Expiry clock tolerance in seconds, 0–60 (reference value 30). One shared variable feeds `auth-api`, `api-a`, and `api-b`, keeping them consistent. `auth-api` refuses to start, naming only `Jwt:ClockSkewSeconds`, when it is missing or out of range. |

Everything else in `.env.example` is unchanged.

## Existing databases

The new migration adds the `IsEnabled` column to users when `auth-api` starts. Existing rows,
including the built-in administrator, become enabled, so an upgraded installation keeps working
without any manual step.

## Administrative endpoints

All eleven operations live under `/api/admin`, require a valid access token, and require the
`Administrator` role. Any of them answers `401` without a valid token, `403` with a valid token
that lacks the role, and `503` when the database is unavailable. Request bodies reject unknown
members with `400`. Responses never contain password hashes, security stamps, tokens, or keys.

| Operation | Success | Other outcomes |
|---|---|---|
| `GET /api/admin/users` | `200` array, sorted by email | |
| `GET /api/admin/users/{id}` | `200` | `404` |
| `POST /api/admin/users` (`email`, `password`, optional `enabled` default `true`, optional `roles`) | `201` + `Location` | `400` invalid body, weak password, or nonexistent role (nothing created); `409` email already used (any letter case) |
| `PATCH /api/admin/users/{id}` (`email` only) | `200` | `400` any other member or invalid body; `404`; `409` email already used |
| `PUT /api/admin/users/{id}/roles` (`roles`: the complete set) | `200` | `400` invalid body or nonexistent role (nothing applied); `404`; `409` would leave no enabled administrator |
| `POST /api/admin/users/{id}/enable` | `200` (idempotent) | `404` |
| `POST /api/admin/users/{id}/disable` | `200` (idempotent) | `404`; `409` would leave no enabled administrator |
| `GET /api/admin/roles` | `200` array, sorted by name | |
| `POST /api/admin/roles` (`name`) | `201` + `Location` | `400`; `409` name already used |
| `PATCH /api/admin/roles/{id}` (`name`) | `200` | `400`; `404`; `409` name already used, or the `Administrator` role |
| `DELETE /api/admin/roles/{id}` | `204` | `404`; `409` role assigned to a user, or the `Administrator` role |

A user view contains `id`, `email`, `enabled`, `isLockedOut`, `lockoutEndUtc`, and `roles`.
Roles are referenced by name (matched ignoring letter case) when creating a user or setting a
user's roles.

## Behavior to know

- **Disabling prevents new logins only.** A disabled account gets the same generic `401` as an
  unknown email or a wrong password. Access tokens already issued, including an administrator's,
  stay valid until they expire (15 minutes by default), on `/api/admin/*` and on the consumer
  APIs. Session revocation belongs to a later phase.
- **There is always an enabled administrator.** The last enabled user holding `Administrator`
  cannot be disabled or lose the role, whoever asks and even when requests are concurrent. The
  `Administrator` role itself cannot be renamed or deleted. A disabled administrator does not
  count; a temporarily locked-out one does.
- **Role changes affect new tokens.** A login after a change carries the user's current roles; a
  token issued before keeps the roles it was issued with.
- **Lockout is separate from the enabled state.** `isLockedOut` shows Identity's temporary lockout
  after repeated wrong passwords and clears when it ends.
- **The initial `admin` password still must be replaced.** Password change arrives in a later
  phase; Phase 3 does not provide it.

## Not in this phase

Refresh tokens, sessions, session revocation (`revoke-sessions`), logout, password change,
password recovery, email, physical user deletion, and any change to the consumer APIs.

## Gate G3 verification evidence

Recorded on 2026-10-08 against the working tree of branch `003-phase-3-user-role-administration`
(disposable storage only; `baseline/` was not modified by this verification).

| Evidence | Command | Result |
|---|---|---|
| Build | `dotnet build Authentication.slnx --no-incremental` | PASS — 0 warnings, 0 errors |
| Automated tests (Phases 1–3) | `dotnet test --solution Authentication.slnx` | PASS — 59/59 (17 unit, 42 integration), 0 skipped |
| Administrative access control: no token / forged / expired beyond tolerance / wrong issuer / wrong audience / non-RS256 / unsigned / malformed → `401` problem with `WWW-Authenticate: Bearer`; real non-administrator token → `403`; administrator → authorized on all eleven operations | `AdministrativeAccessTests` + acceptance | PASS |
| User administration: create (default and explicit enabled, initial roles), duplicate email by case `409`, weak password `400`, list/get expose only permitted fields, email-only update, invalid or extra members `400`, unknown id `404` | `UserAdministrationTests` + acceptance | PASS |
| Disable affects login: disabled account gets a `401` identical to a wrong password; enable restores login with the unchanged password; lockout reported independently | `UserAdministrationTests` + acceptance | PASS |
| Roles and assignments: create, duplicate `409`, rename keeps assignments, delete unassigned `204`, delete assigned `409`, `PUT` replaces the set, nonexistent role `400` with nothing applied, new tokens follow roles while old tokens keep theirs (judged at `api-a`) | `RoleAdministrationTests` + acceptance | PASS |
| Last enabled administrator protected: disable, role removal, and `Administrator` role rename/delete refused with `409`; disabled second administrator does not count; locked-out administrator counts; two concurrent disables give exactly one `200` and one `409` | `AdministratorContinuityTests` + `AdministratorContinuity` truth table + acceptance | PASS |
| Concurrency soundness: a deferred (non-immediate) transaction makes the concurrent scenario fail, while the shipped transaction passes repeatedly | mutation check during T035 | PASS |
| Persistence across restart: users, emails, enabled state kept; administrator not re-created; migration applied to a Phase 1 database keeps the administrator enabled | acceptance + manual migration check | PASS |
| No secrets in `auth-api` logs (passwords, access tokens, `PRIVATE KEY`, `"password"`) | acceptance | PASS |
| Phase 1–2 regression | full suite + `tests/acceptance/phase-2.sh` (which runs `phase-1.sh`), run by `phase-3.sh` | PASS |
| Governance inspection | exactly 11 `/api/admin` operations; no change to `Directory.Packages.props` and `Microsoft.AspNetCore.Authentication.JwtBearer` the only newly referenced package; no refresh, session, revocation, blacklist, reset, SMTP, OpenAPI/Scalar, unit-of-work, repository, or mediator types; no password hash, security stamp, or concurrency stamp in Application or Api; the only first-party log call is the startup-failure message (stage, exception type, SQLite code); `src/ReferenceConsumer.Api` and the Phase 1 login and health code unchanged; Compose has only `auth-api`, `api-a`, `api-b` and no volume or service was added | PASS |

Acceptance output (abridged):

```text
PASS  access control (401 anonymous, 200 administrator) and user creation/read-back
PASS  disable refuses login with a body identical to a wrong password; enable restores it
PASS  role created and assigned; the new token is 403 on the admin API and carries Operator at api-a
PASS  deleting an assigned role is refused (409); after removal it is deleted (204)
PASS  the sole enabled administrator cannot be disabled or lose its role (409, unchanged)
PASS  restart: users, emails, and enabled state persisted; the administrator was not re-created
PASS  auth-api logs contain no passwords, access tokens, or private key material
PASS  Phase 2 acceptance regression (includes Phase 1)
Phase 3 acceptance: ALL PASS
```
