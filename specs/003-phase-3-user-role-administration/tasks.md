---
description: "Executable task list for Phase 3 — User and Role Administration"
---

# Tasks: Phase 3 — User and Role Administration

**Input**: Design documents in specs/003-phase-3-user-role-administration/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/authentication-api-admin.openapi.yaml, and quickstart.md

**Active roadmap phase / gate**: Roadmap Phase 3, Gate G3 (Roadmap section 9).

**Tests**: Tests are mandatory for current critical behavior (spec NFR-001). Extend the two existing test projects only. Use the four consolidated integration scenario classes and the two unit tests defined in the plan, with `AuthenticationApiFactory` over a temporary SQLite file, real Identity, real JwtBearer, and tokens obtained from real login (plus `TestTokenMinter` for negative cases). Do not add a test project, a mocking library, sleeps, or tests for future phases. Phase 1 and Phase 2 test classes are regression and keep their assertions unchanged.

**Organization**: Tasks are ordered by the four user stories. The access-control, configuration, schema, and continuity-rule foundations are built first so no administrative operation can ever exist unprotected. Each operation is built together with the integrity rule it must honor (disable with the last-enabled-administrator rule, role replacement and role rename/delete with the same rule and the `Administrator` protection), so US4 proves those protections end to end rather than adding them late.

## Format: [ID] [P?] [Story] Description

- [P] means the task can run in parallel after its stated prerequisites because it changes different files.
- [US#] maps a task to its feature user story.
- Every task names implementation, test, configuration, or documentation paths.
- src/ReferenceConsumer.Api MUST remain unchanged in this phase. The Phase 1 login request/response and the access-token claims MUST remain unchanged.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the already-pinned validator package available to Authentication API; no behavior.

- [X] T001 Add a `PackageReference` to the already centrally pinned `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.12, Technical Constraints section 33) in src/Authentication.Infrastructure/Authentication.Infrastructure.csproj; add no new package version to Directory.Packages.props and no other package.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Supply configuration, signing-key sharing, the enabled-state schema, token validation and the administrative policy, the Domain continuity rule, shared result/HTTP types, and test plumbing needed by every story. No administrative endpoint is completed in this phase.

**⚠️ CRITICAL**: Complete this phase before starting user-story implementation.

- [X] T002 Add the required setting `Jwt:ClockSkewSeconds` in src/Authentication.Infrastructure/Security/JwtOptions.cs, src/Authentication.Infrastructure/DependencyInjection.cs (validation: integer from 0 to 60 inclusive; no default in code; a missing or out-of-range value terminates startup with a message that names only the setting, never its value), and src/Authentication.Api/appsettings.json (empty placeholder, as for the consumer).
- [X] T003 [P] Pass the shared clock tolerance to `auth-api` in compose.yml (`Jwt__ClockSkewSeconds: "${AUTH_JWT_CLOCK_SKEW_SECONDS:-30}"`, the same variable `api-a` and `api-b` already use) and note in .env.example that `AUTH_JWT_CLOCK_SKEW_SECONDS` is now shared by `auth-api` and both consumers; add no service, volume, or other variable.
- [X] T004 [P] Extend tests/Authentication.UnitTests/Infrastructure/JwtOptionsTests.cs with one parameterized test that builds Authentication API configuration in memory (with a temporary readable private-key file) and shows `Jwt:ClockSkewSeconds` is accepted for 0 and 60 and rejected, naming `Jwt:ClockSkewSeconds` and not its value, when missing, non-numeric, -1, or 61.
- [X] T005 Create src/Authentication.Infrastructure/Security/RsaSigningKey.cs, a singleton that loads the private PEM once and exposes the signing credentials and a public-only `RsaSecurityKey`; make src/Authentication.Infrastructure/Security/JwtAccessTokenIssuer.cs consume it with unchanged token output and unchanged startup failure messages (an unusable key still fails startup naming `Jwt:PrivateKeyPath`, resolving the issuer before initialization); register it in src/Authentication.Infrastructure/DependencyInjection.cs; the existing LoginAndJwtTests and BootstrapAndHealthTests must keep passing.
- [X] T006 Introduce src/Authentication.Infrastructure/Identity/ApplicationUser.cs deriving from `IdentityUser<string>` with exactly one added property `bool IsEnabled` (no model-level default value, so a user created disabled is stored disabled); switch src/Authentication.Infrastructure/Persistence/AuthenticationDbContext.cs (keep the unique `EmailIndex` on `NormalizedEmail`), src/Authentication.Infrastructure/Persistence/DatabaseInitializer.cs (the built-in administrator is created with `IsEnabled = true`), src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs (type change only, no behavior change), and the `AddIdentityCore` registration in src/Authentication.Infrastructure/DependencyInjection.cs to `ApplicationUser`.
- [X] T007 Generate the single EF Core migration `AddUserEnabledState` under src/Authentication.Infrastructure/Persistence/Migrations/ that creates `AspNetUsers.IsEnabled INTEGER NOT NULL` and edit the generated migration-level default to `true` so existing rows, including the built-in administrator, stay enabled; it must change no other column, index, or data and add no table; confirm the model snapshot contains no database default for `IsEnabled`.
- [X] T008 [P] Update the `UserManager<IdentityUser<string>>` type references to `UserManager<ApplicationUser>` in tests/Authentication.IntegrationTests/Scenarios/BootstrapAndHealthTests.cs and tests/Authentication.IntegrationTests/Scenarios/BootstrapLifecycleTests.cs with no change to their assertions.
- [X] T009 Implement administrative token validation in src/Authentication.Infrastructure/Security/JwtValidationRegistration.cs and register it from src/Authentication.Infrastructure/DependencyInjection.cs: JwtBearer as the default scheme using the public key from `RsaSigningKey` and the same parameters as src/ReferenceConsumer.Api/Security/JwtValidationRegistration.cs (RS256 only; signed tokens and expiry required; issuer, audience, lifetime, and signing key validated; `ClockSkew` from `Jwt:ClockSkewSeconds`; `NameClaimType = "sub"`, `RoleClaimType = "role"`; `MapInboundClaims = false`; `IncludeErrorDetails = false`; no Authority or MetadataAddress); add the authorization policy named `Administrator` requiring the role `Administrator`; `OnChallenge` writes a fixed `401` ProblemDetails with `WWW-Authenticate: Bearer` and no error description, `OnForbidden` writes a fixed `403` ProblemDetails; never log tokens or key material.
- [X] T010 [P] Create the pure Domain rule in src/Authentication.Domain/Administration/AdministratorContinuity.cs, `Permits(isEnabledAdministratorNow, remainsEnabledAdministrator, enabledAdministratorCount)`: permitted unless the target is an enabled administrator now, will not remain one, and the count is 1 (an enabled administrator is a user with `IsEnabled = true` holding the `Administrator` role; lockout does not affect this status); with no reference to ASP.NET Core, Identity, EF Core, or SQLite; and add tests/Authentication.UnitTests/Domain/AdministratorContinuityTests.cs covering the full truth table (not enabled admin now → permitted; yes/yes → permitted; yes/no with count 1 → refused; yes/no with count ≥ 2 → permitted).
- [X] T011 [P] Create the shared Application result in src/Authentication.Application/Features/Administration/AdministrationError.cs (`Invalid`, `NotFound`, `Conflict`) and src/Authentication.Application/Features/Administration/AdministrationResult.cs (`AdministrationResult<T>` carrying `Value` on success, otherwise the `Error` plus a fixed, safe `Detail`; no Identity, EF, or SQLite text).
- [X] T012 Create the shared HTTP boundary in src/Authentication.Api/Features/Administration/AdministrationEndpoints.cs (one `/api/admin` route group with `RequireAuthorization("Administrator")`, mapped by `MapAdministrationEndpoints`, so every administrative endpoint is protected by construction), src/Authentication.Api/Features/Administration/AdministrationRequests.cs (`ReadAsync<T>` requiring a JSON content type and mapping `JsonException` to `null`), and src/Authentication.Api/Features/Administration/AdministrationResults.cs (translate `AdministrationResult<T>` to `200`, `201` with `Location`, `204`, and fixed-text ProblemDetails `400`/`404`/`409`, and `DbException` to `503`); wire `UseAuthentication()`, `UseAuthorization()`, and `MapAdministrationEndpoints()` in src/Authentication.Api/Program.cs while keeping the Phase 1 login and health endpoints anonymous and unchanged.
- [X] T013 [P] Update test plumbing in tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs (set `Jwt__ClockSkewSeconds` to 30 for the host) and tests/Authentication.IntegrationTests/Infrastructure/TestTokenMinter.cs (add a constructor that signs with an existing private PEM, for example the Authentication API test key, so expired, wrong-issuer, and wrong-audience tokens carry a valid signature); existing members and Phase 1/2 tests are unchanged.
- [X] T014 [P] Create tests/Authentication.IntegrationTests/Infrastructure/AdminTestSupport.cs with small shared helpers used by the Phase 3 scenario classes only: log in through `/api/auth/login` and return a bearer-authenticated client, issue the built-in administrator token, and create a user and log in as it; do not duplicate logic across the four scenario classes and add no mocks. The create-user helper calls `POST /api/admin/users`, so it compiles here but can only be exercised after T020.

**Checkpoint**: Solution builds, the unchanged Phase 1 and Phase 2 suites still pass, `auth-api` starts only with a valid clock tolerance, existing databases migrate with the administrator enabled, and an unauthenticated call to any path under `/api/admin` cannot reach an administrative operation.

---

## Phase 3: User Story 1 - Administrators Manage Users (Priority: P1) 🎯 MVP

**Goal**: An authenticated administrator creates, lists, retrieves, and updates the email of users; every administrative call requires a valid token with the `Administrator` role (`401` / `403`).

**Independent Test**: With the Phase 1 administrator token, create a user, list, retrieve it, and update its email; repeat a representative call with no token and with a valid non-administrator token.

### Tests for User Story 1

- [X] T015 [US1] Write the failing access scenarios in tests/Authentication.IntegrationTests/Scenarios/AdministrativeAccessTests.cs over a single operation list that this task starts with the four user operations (`GET /api/admin/users`, `GET /api/admin/users/{id}`, `POST /api/admin/users`, `PATCH /api/admin/users/{id}`) and that later stories extend: each operation without a token → `401`; on one representative operation, a token with a forged signature, expired well beyond the tolerance, wrong issuer, wrong audience, and RS512 → `401` ProblemDetails with `WWW-Authenticate: Bearer` and no error description, and a token expired within the tolerance → accepted; a real login token of a user without the `Administrator` role → `403` ProblemDetails on each operation; the real administrator token → authorized; and the Phase 1 login request and response shapes asserted unchanged.
- [X] T016 [US1] Write the failing user scenarios in tests/Authentication.IntegrationTests/Scenarios/UserAdministrationTests.cs: create with default enabled state, explicit `enabled`, and initial roles (`201` with `Location`); email differing only by case → `409`; password violating the policy → `400` with the list unchanged; initial role that does not exist → `400` with no user created; list and get expose only `id`, `email`, `enabled`, `isLockedOut`, `lockoutEndUtc`, and `roles` and never a password hash, security stamp, or token; `PATCH` of the email → login works with the new email and fails with the old one; `PATCH` carrying `enabled`, `roles`, `password`, or any unknown member → `400` with nothing changed; `PATCH {}`, a malformed JSON body, and a non-JSON content type (`text/plain`) on `PATCH` and `POST /api/admin/users` → `400` with nothing changed; `PATCH` to another user's email → `409`; `PATCH` to the user's own current email → `200`; unknown id on get and patch → `404`.

### Implementation for User Story 1

- [X] T017 [P] [US1] Create the Users slice contracts in src/Authentication.Application/Features/Users/UserView.cs (`Id`, `Email`, `Enabled`, `IsLockedOut`, `LockoutEndUtc?` set only when locked, `Roles` as names sorted by normalized name; no other member), src/Authentication.Application/Features/Users/CreateUserCommand.cs (`Email`, `Password`, `Enabled` already defaulted to `true`, `Roles` names, may be empty), and src/Authentication.Application/Features/Users/IUserAdministration.cs with `ListAsync`, `FindAsync`, `CreateAsync`, and `UpdateEmailAsync` returning `AdministrationResult<UserView>`; later stories add their own methods to the port.
- [X] T018 [P] [US1] Create src/Authentication.Api/Features/Users/CreateUserRequest.cs and src/Authentication.Api/Features/Users/UpdateUserRequest.cs, both carrying `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` so any unknown member fails with `400`, with an `IsValid` shape check: email required, valid syntax, at most 256 characters; password required and nonblank; `enabled` optional and defaulting to `true`; role names nonblank and at most 256 characters; `UpdateUserRequest` accepts the `email` member only.
- [X] T019 [US1] Implement `ListAsync`, `FindAsync`, `CreateAsync`, and `UpdateEmailAsync` in src/Authentication.Infrastructure/Identity/UserAdministration.cs and register it in src/Authentication.Infrastructure/DependencyInjection.cs: every mutation runs in one database transaction (SQLite takes the write lock first) so a refusal leaves no partial state; resolve all requested role names after Identity normalization (any missing → `Invalid`, duplicates collapse) before creating; create with `CreateAsync(user, password)` and `UserName` equal to the generated `Id`, mapping Identity error codes (`DuplicateEmail`/`DuplicateUserName` → `Conflict`; `Password*`/`InvalidEmail` → `Invalid`) and never returning Identity descriptions; `UpdateEmailAsync` uses `SetEmailAsync`, treats the user's own current email as a no-op success, and maps a unique-index violation to `Conflict`; lockout is computed from `TimeProvider`; users sorted by normalized email.
- [X] T020 [US1] Implement the user endpoints in src/Authentication.Api/Features/Users/UserAdministrationEndpoints.cs and mount them inside the `/api/admin` group from src/Authentication.Api/Features/Administration/AdministrationEndpoints.cs: `GET /api/admin/users` (`200` array), `GET /api/admin/users/{id}` (`200` or `404`), `POST /api/admin/users` (`201` with `Location` and the created view), `PATCH /api/admin/users/{id}` (`200` with the view), per specs/003-phase-3-user-role-administration/contracts/authentication-api-admin.openapi.yaml; add no endpoint beyond the Phase 3 contract.
- [X] T021 [US1] Make the T015 and T016 scenarios pass using real SQLite, real Identity, real JwtBearer, and real login tokens; verify external responses and persisted behavior rather than mocked substitutes.
- [X] T022 [US1] Create the disposable Compose demonstration tests/acceptance/phase-3.sh modeled on tests/acceptance/phase-2.sh (own `COMPOSE_PROJECT_NAME` and host ports, disposable external storage, `auth-api` + `api-a` + `api-b`): log in as `admin@local.invalid` / `admin`; `GET /api/admin/users` without a token → `401` and with the administrator token → `200`; create a user and read it back.

**Checkpoint**: Users can be created, listed, retrieved, and updated by an administrator, and every administrative call is rejected with `401`/`403` unless it carries an administrator token.

---

## Phase 4: User Story 2 - Disable and Re-enable Accounts (Priority: P2)

**Goal**: An administrator disables a user so login is refused with the generic credential failure, and re-enables it so the unchanged password works again; the last enabled administrator can never be disabled.

**Independent Test**: Create a user, confirm login works, disable it and confirm login returns the same `401` as a wrong password, re-enable it and confirm login works with the unchanged password.

### Tests for User Story 2

- [X] T023 [US2] Extend tests/Authentication.IntegrationTests/Scenarios/UserAdministrationTests.cs and tests/Authentication.IntegrationTests/Scenarios/AdministrativeAccessTests.cs (add `POST /api/admin/users/{id}/enable` and `/disable` to the operation list): disable → login with the correct credentials returns `401` whose status and body equal those of a wrong-password login; disabling twice and enabling twice are idempotent (`200`, unchanged); enable → login `200` with the unchanged password; email, password, and roles survive a disable/enable cycle; a user created with `enabled: false` cannot log in; immediately after enough wrong passwords to trigger Identity lockout, the user view shows `isLockedOut` true and a `lockoutEndUtc` later than the request time, independent of the enabled state (use the default system-clock host: Identity 10 lockout and JwtBearer both use the system clock, so a `ControlledTimeProvider` must not be used in Phase 3 scenarios, and the clock is never advanced); enable and disable of an unknown id → `404`.

### Implementation for User Story 2

- [X] T024 [US2] Make src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs refuse disabled accounts: check `IsEnabled` only after the normal password verification and failure accounting and return `null` (the existing generic `401`) when disabled, so a disabled account spends the same hashing work as a wrong password and the failed-attempt counter is not reset. In the same file, make the locked-out branch perform one password-hasher verification of the presented password against the user's stored hash (result ignored; not `CheckPasswordAsync`, no failure counted) before returning `null`, so unknown email, wrong password, locked, and disabled each spend exactly one hash verification (FR-009, FR-LOGIN-015, NFR-SEC-ENUM-004). The login request, response, and token contract are unchanged, and the existing LoginAndJwtTests lockout assertions keep passing.
- [X] T025 [US2] Add `SetEnabledAsync(id, enabled)` to src/Authentication.Application/Features/Users/IUserAdministration.cs and implement it in src/Authentication.Infrastructure/Identity/UserAdministration.cs inside the transaction: succeed without writing when the state is unchanged; when disabling, count enabled administrators and apply `AdministratorContinuity` so disabling the last enabled administrator returns `Conflict` ("The operation would leave no enabled administrator.") with no change; touch only `IsEnabled` (never the password hash, security stamp, email, or roles).
- [X] T026 [US2] Add `POST /api/admin/users/{id}/enable` and `POST /api/admin/users/{id}/disable` to src/Authentication.Api/Features/Users/UserAdministrationEndpoints.cs, returning `200` with the resulting user view or `404`/`409`, per the OpenAPI contract.
- [X] T027 [US2] Make the T023 scenarios pass and extend tests/acceptance/phase-3.sh: create a user, log in, disable it (login → `401` with a body identical to a wrong-password login), enable it (login → `200` again).

**Checkpoint**: Disabling withdraws login without altering the account, re-enabling restores it, and the Phase 1 login contract and Phase 2 consumers are unchanged.

---

## Phase 5: User Story 3 - Manage Roles and Assignments (Priority: P3)

**Goal**: An administrator creates, lists, renames, and deletes roles, and sets which roles a user holds; assignments replace the user's complete role set.

**Independent Test**: Create a role, assign it to a user, confirm the next login token carries it, remove it, delete the unassigned role, and confirm deleting an assigned role is refused.

### Tests for User Story 3

- [X] T028 [US3] Write the failing role scenarios in tests/Authentication.IntegrationTests/Scenarios/RoleAdministrationTests.cs and extend the operation list in tests/Authentication.IntegrationTests/Scenarios/AdministrativeAccessTests.cs with `GET`/`POST /api/admin/roles`, `PATCH`/`DELETE /api/admin/roles/{id}`, and `PUT /api/admin/users/{id}/roles` (so all eleven operations are covered): create a role; a case-variant duplicate → `409`; `PUT` roles replaces the set and the user's next login token carries exactly those `role` claims; omitting a role removes it from that user only; an empty list leaves the user with no roles; a nonexistent role → `400` with the roles unchanged; an unknown user → `404`; rename keeps assignments and new tokens carry the new name; a colliding rename → `409`; delete of an unassigned role → `204`; delete of an assigned role → `409` with nothing changed; unknown role id → `404`; `PUT` roles without the `roles` member, `POST`/`PATCH` roles with a blank or 257-character name, and an unknown member in either body → `400`; a token issued before a role change keeps its original role claims and is judged by them at `api-a` (host `ReferenceConsumerFactory` with the Authentication API public key).

### Implementation for User Story 3

- [X] T029 [P] [US3] Create src/Authentication.Application/Features/Roles/RoleView.cs (`Id`, `Name`) and src/Authentication.Application/Features/Roles/IRoleAdministration.cs (`ListAsync`, `CreateAsync(name)`, `RenameAsync(id, name)`, `DeleteAsync(id)` returning `AdministrationResult<RoleView>`), and add `ReplaceRolesAsync(id, roles)` to src/Authentication.Application/Features/Users/IUserAdministration.cs.
- [X] T030 [P] [US3] Create src/Authentication.Api/Features/Roles/RoleNameRequest.cs and src/Authentication.Api/Features/Users/ReplaceUserRolesRequest.cs with `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` and an `IsValid` shape check: role name required, trimmed, 1–256 characters; `roles` required (an empty list is valid), each name nonblank and at most 256 characters.
- [X] T031 [US3] Implement src/Authentication.Infrastructure/Identity/RoleAdministration.cs and `ReplaceRolesAsync` in src/Authentication.Infrastructure/Identity/UserAdministration.cs, registered in src/Authentication.Infrastructure/DependencyInjection.cs, each mutation in one transaction: create via `RoleManager.CreateAsync` mapping `DuplicateRoleName` to `Conflict`; rename via `SetRoleNameAsync` + `UpdateAsync` mapping a collision to `Conflict`; delete via `DeleteAsync` only when the role has no assignments (assigned → `Conflict`: "The role is assigned to one or more users."); rename or delete of the role with the fixed `Administrator` id (`7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f01`) → `Conflict` ("The Administrator role cannot be renamed or deleted."); `ReplaceRolesAsync` resolves the supplied names after normalization (duplicates collapse; any missing → `Invalid` with nothing applied), computes the diff, applies `AdministratorContinuity` when `Administrator` would be removed from an enabled user (refused → `Conflict` with no change), then removes and adds roles in the same transaction; an unknown user or role id → `NotFound`.
- [X] T032 [US3] Add src/Authentication.Api/Features/Roles/RoleAdministrationEndpoints.cs (`GET /api/admin/roles` → `200` array, `POST /api/admin/roles` → `201` with `Location`, `PATCH /api/admin/roles/{id}` → `200`, `DELETE /api/admin/roles/{id}` → `204`) and `PUT /api/admin/users/{id}/roles` in src/Authentication.Api/Features/Users/UserAdministrationEndpoints.cs (`200` with the user view), mounted inside the `/api/admin` group, per the OpenAPI contract; the endpoint surface is now exactly the eleven Roadmap section 9.2 operations.
- [X] T033 [US3] Make the T028 scenarios pass and extend tests/acceptance/phase-3.sh: create role `Operator`, create a user holding it, log in as that user, confirm `403` on `/api/admin/users` and `200` from `api-a` `/api/caller` reporting role `Operator`; deleting `Operator` while assigned → `409`; set the user's roles to `[]`, then delete `Operator` → `204`.

**Checkpoint**: All eleven administrative operations work, role claims in new tokens follow assignments, and tokens already issued keep the claims they carried.

---

## Phase 6: User Story 4 - The System Always Keeps an Enabled Administrator (Priority: P4)

**Goal**: No operation, from any administrator and by any route, can leave the system without an enabled user holding the `Administrator` role, and the canonical `Administrator` role cannot be renamed or deleted.

**Independent Test**: With a single enabled administrator, attempt to disable it, remove its role, and rename or delete the `Administrator` role (all refused, state unchanged); with two enabled administrators, one can be disabled and the remaining one cannot.

### Tests for User Story 4

- [X] T034 [US4] Write the failing continuity scenarios in tests/Authentication.IntegrationTests/Scenarios/AdministratorContinuityTests.cs: with the built-in administrator as the sole enabled administrator, disabling itself, `PUT` roles without `Administrator`, and renaming or deleting the `Administrator` role each return `409` and leave the user view and role unchanged; a second administrator that is disabled does not count (disabling or demoting the first still returns `409`); with two enabled administrators, one can be disabled or demoted (`200`) and the remaining one then cannot (`409`); a locked-out administrator still counts as enabled; with exactly two enabled administrators, two simultaneous disable requests (one per administrator) produce exactly one `200` and one `409` and exactly one enabled administrator remains.

### Implementation for User Story 4

- [X] T035 [US4] Make the T034 scenarios pass, correcting any gap found in src/Authentication.Infrastructure/Identity/UserAdministration.cs or src/Authentication.Infrastructure/Identity/RoleAdministration.cs rather than weakening the test; confirm that the transaction used by every mutation takes SQLite's write lock before the first read (the concurrent double-disable must yield one success and one conflict against real SQLite, not a simulated lock) and that a waiting writer relies on Microsoft.Data.Sqlite's default busy retry (default 30-second command timeout, not overridden in the connection string) so the concurrent scenario observes exactly `200` and `409` and never `503`.
- [X] T036 [US4] Extend tests/acceptance/phase-3.sh: disabling the sole enabled administrator → `409`; restart `auth-api` and confirm the created user, its email, and its enabled state persisted and the built-in administrator was not re-created (the migration applied once, idempotently); scan `docker compose logs auth-api` and fail if any password used by the script, any access token it obtained, or `PRIVATE KEY` appears (NFR-002); then run `docker compose down -v` and finish with the regression block that runs tests/acceptance/phase-2.sh (which runs phase-1.sh) with the Phase 3 variables unset, modeled on the regression block at the end of phase-2.sh, printing `Phase 3 acceptance: ALL PASS` only after it succeeds.

**Checkpoint**: The continuity invariant holds across every route, including concurrent requests, and the Phase 3 capability is complete.

---

## Phase 7: Polish & Gate G3 Evidence

**Purpose**: Complete only Phase 3 documentation, regression, security review, and G3 evidence.

- [X] T037 [P] Create docs/phase-3-operations.md and reconcile .env.example and specs/003-phase-3-user-role-administration/quickstart.md with delivered behavior: the eleven endpoints and their statuses, the new shared `AUTH_JWT_CLOCK_SKEW_SECONDS` requirement for `auth-api`, that existing databases migrate automatically with the administrator staying enabled, that a disabled or demoted administrator's already issued token stays valid until it expires, and that `revoke-sessions`, refresh, password change, and recovery remain later phases; exclude Phase 4–8 workflows.
- [X] T038 Run `dotnet build Authentication.slnx --no-incremental` and `dotnet test --solution Authentication.slnx`, correcting all first-party compiler/analyzer warnings and Phase 1, Phase 2, or Phase 3 regressions in the relevant src/ or tests/ file rather than suppressing them globally.
- [X] T039 Run tests/acceptance/phase-3.sh (which ends by running tests/acceptance/phase-2.sh and, through it, tests/acceptance/phase-1.sh) and the quickstart.md G3 procedure against disposable external storage; record build, tests, administrative access control, user lifecycle, enable/disable login effect, roles and assignments, last-administrator protection, restart persistence, and Phase 1–2 regression evidence in docs/phase-3-operations.md without modifying normative files under baseline/.
- [X] T040 Inspect src/, tests/, compose.yml, and .env.example for Phase 3 governance compliance: exactly the eleven Roadmap section 9.2 administrative operations exist (no `revoke-sessions`, deletion, or other route under `/api/admin`); no new package version and `Microsoft.AspNetCore.Authentication.JwtBearer` is the only newly referenced package; no refresh, session, revocation, blacklist, password-reset, email, OpenAPI/Scalar runtime, unit-of-work, generic repository, or handler-forwarding types or tables exist; administrative responses contain no password hash, security stamp, token, or key; no first-party log call in src/Authentication.Infrastructure or src/Authentication.Api writes a password, token, request body, or key material (NFR-002), and no HTTP request/body logging middleware is enabled; `git diff` shows src/ReferenceConsumer.Api and the Phase 1 login and health contracts unchanged; Compose still has only `auth-api`, `api-a`, and `api-b` with no new volume or service.
- [ ] T041 Only after T001-T040 are complete, verify every applicable Roadmap section 9.5 Gate G3 acceptance criterion and confirm build, tests, startup, administrative workflow, disable-affects-login, last-administrator protection, restart persistence, and Phase 1–2 regression pass; present the evidence and obtain explicit approval from the project owner; only then record the Gate G3 approval, update the Phase 3 status and progress records in baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md (including the dashboard, section 9.5 criteria, progress log, and current state), update the Gate G3 closure section in specs/003-phase-3-user-role-administration/checklists/requirements.md with evidence references, record a decision only if an actual architectural or roadmap-impacting decision occurred, and create the identifiable Phase 3 closing commit. These explicit, reviewed, version-controlled status/checklist/progress updates are permitted and required for closure; do not mark Phase 3 complete before verification and approval, and do not alter normative requirements, technical constraints, architecture, scope, or implementation sequencing through a tracking update.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on T001 and blocks every user story. Within it: T004 and T013 follow T002; T005 and T006 are independent of each other; T007 follows T006; T008 follows T006; T009 follows T005 and T002; T012 follows T009 and T011.
- **US1 (Phase 3)**: Depends on T002–T014 and produces the first working administrative operations.
- **US2 (Phase 4)**: Depends on US1 (it extends the same adapter, endpoint file, and test classes) and on T010 for the continuity rule.
- **US3 (Phase 5)**: Depends on US1 and on T010; extends the same adapter, endpoint file, and test classes as US1–US2.
- **US4 (Phase 6)**: Depends on US2 and US3 (it proves the protections built into disable, role replacement, and role rename/delete).
- **Polish/G3 (Phase 7)**: Depends on US1, US2, US3, and US4 completion.

### User Story Dependencies

- **US1 (P1)**: First independently executable vertical slice; no feature-story dependency.
- **US2 (P2)**: Adds enabled-state behavior to the US1 user views and endpoints; adds no new table or service.
- **US3 (P3)**: Adds the roles slice and the role-replacement operation; adds no role hierarchy.
- **US4 (P4)**: Adds no new endpoint; it verifies, and hardens where tests show a gap, the integrity rules already enforced by US2 and US3.

### Parallel Opportunities

- T003 and T004 can proceed in parallel after T002, because they touch compose/documentation and a unit-test file.
- T008, T010, T011, T013, and T014 can proceed in parallel once their stated prerequisites are done, because they touch different files.
- T017 and T018 can proceed in parallel within US1, and T029 and T030 within US3, because they touch independent Application and Api contract files.
- T037 can proceed in parallel with final code review once configuration paths are stable.
- Tasks marked [P] touch distinct files; the four scenario classes and the two shared adapters are extended by several stories and therefore run sequentially.

## Parallel Example: User Story 1

Task: Create the Users slice contracts in src/Authentication.Application/Features/Users/

Task: Create the user request types in src/Authentication.Api/Features/Users/

## Implementation Strategy

### MVP First (US1)

1. Complete Setup and Foundational work.
2. Complete US1 through T022.
3. Validate on disposable storage that an administrator can create, list, retrieve, and update users and that anonymous and non-administrator calls get `401` and `403`.
4. Do not claim disable, role, or integrity completion until later story checkpoints pass.

### Incremental Delivery

1. Foundations make every administrative path authenticated, authorized, and schema-ready.
2. US1 adds user administration, US2 adds disable/enable with its login effect, US3 adds roles and assignments.
3. US4 proves the last-enabled-administrator and `Administrator`-role protections end to end, including concurrency.
4. Polish records Gate G3 evidence; implementation stops at the Phase 3 boundary.

## Notes

- All tasks use current-phase behavior only; no task authorizes refresh tokens, sessions, session revocation, logout, token blacklists, password change or recovery, SMTP, physical user deletion, extra administrative endpoints, or consumer-API changes. Disabling prevents new logins only.
- Program.cs remains a composition root. Identity, EF, SQLite, and JwtBearer implementations stay in Infrastructure; Application stays independent of Infrastructure; Domain receives only the pure continuity rule. Endpoints live with their feature slice (`Features/Users`, `Features/Roles`) and mount through the single protected `Features/Administration` group.
- The eleven operations are exactly Roadmap section 9.2; the `revoke-sessions` route in SRS section 35.2 belongs to Phase 4.
- A task is not complete merely because its checkbox is checked: it must meet its traced requirement, preserve existing tests, and satisfy its phase checkpoint.
