---

description: "Task list for Phase 4 renewable sessions, refresh rotation, and logout"
---

# Tasks: Phase 4 — Refresh Tokens, Renewable Sessions and Logout

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md), and
[authentication-api-sessions.openapi.yaml](contracts/authentication-api-sessions.openapi.yaml)

**Prerequisites**: Approved Phase 4 plan and clarified specification.

**Tests**: Required. Use existing xUnit v3 projects, `WebApplicationFactory`, real SQLite, and
`ControlledTimeProvider`; do not use sleeps, EF InMemory, new test projects, or unnecessary mocks.

**Organization**: Tasks are grouped by independently verifiable user story. Shared persistence,
session invariants, configuration, and API security boundary work is completed first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites because it affects a distinct file.
- **[US#]**: The user story served by a task. Shared setup and foundation work has no story label.

## Phase 1: Setup and Contract Alignment

**Purpose**: Establish the Phase 4 configuration and public-contract baseline without changing
unrelated deployment or consumer behavior.

- [ ] T001 Add externally validated `RefreshSession:LifetimeDays` (default `7`) and required `Security:FrontendOrigin` configuration in `src/Authentication.Api/appsettings.json`, propagate `Security__FrontendOrigin` and the optional lifetime override through `compose.yml`, and document runnable example values in `.env.example`; invalid or missing required configuration must fail fast without exposing its value.
- [ ] T002 [P] Keep the Phase 4 contract aligned by documenting `auth_refresh` cookie attributes, the Origin requirement, request/response schemas, and every applicable 200/204/400/401/403/404/503 outcome in `specs/004-phase-4-refresh-sessions-logout/contracts/authentication-api-sessions.openapi.yaml`.
- [ ] T003 [P] Extend reusable test configuration and cookie/origin request helpers for Phase 4 in `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs`.

---

## Phase 2: Foundational Session State and Security Boundary

**Purpose**: Create the Auth API-owned session model, persistence lifecycle, secure credential
primitive, and application contracts that block all session stories.

**⚠️ CRITICAL**: Complete this phase before implementing any user story.

- [ ] T004 Define renewable-family state and irreversible revocation reasons in `src/Authentication.Domain/Sessions/RenewableSessionFamily.cs` with required immutable user, UTC creation, absolute expiry, and write-once revocation state.
- [ ] T005 Define refresh-credential chain state in `src/Authentication.Domain/Sessions/RefreshCredential.cs` with a required `byte[32]` SHA-256 verifier, write-once consumption/revocation, and at most one replacement in the same family.
- [ ] T006 [P] After T004, add pure state-transition invariant coverage for active/expired/revoked families and consumed credential replacement in `tests/Authentication.UnitTests/Domain/RenewableSessionFamilyTests.cs`.
- [ ] T007 [P] After T005, add pure state-transition invariant coverage for credential consumption and one-replacement rules in `tests/Authentication.UnitTests/Domain/RefreshCredentialTests.cs`.
- [ ] T008 Add session issuance, rotation, lookup, and family-revocation application contracts under `src/Authentication.Application/Features/Sessions/`, keeping `IRenewableSessionStore` in `IRenewableSessionStore.cs` and every command/result/outcome as one top-level type in its own matching file.
- [ ] T009 Add `RefreshSessionOptions` validation for a positive lifetime whose conversion and addition cannot overflow, plus a syntactically valid absolute configured frontend origin, in `src/Authentication.Infrastructure/Sessions/RefreshSessionOptions.cs`; do not invent a policy maximum absent from the specification.
- [ ] T010 Implement 256-bit CSPRNG base64url credential creation, strict malformed-value rejection, and SHA-256 verifier derivation without logging raw values or digests in `src/Authentication.Infrastructure/Sessions/RefreshCredentialProtector.cs`.
- [ ] T011 Add `RenewableSessionFamily` and `RefreshCredential` entity mappings, unique verifier and required family/user indexes, restrictive foreign keys, and data-model constraints to `src/Authentication.Infrastructure/Persistence/AuthenticationDbContext.cs`.
- [ ] T012 Generate the Phase 4 migration with `dotnet ef migrations add AddRenewableSessions` and version-control all generated artifacts under `src/Authentication.Infrastructure/Persistence/Migrations/`: the timestamped `*AddRenewableSessions.cs`, matching `*AddRenewableSessions.Designer.cs`, and updated `AuthenticationDbContextModelSnapshot.cs`; verify startup discovers and applies it.
- [ ] T013 Register validated session options, credential protection, session store, and their existing startup migration lifecycle in `src/Authentication.Infrastructure/DependencyInjection.cs` and `src/Authentication.Api/Program.cs`.
- [ ] T014 Add real temporary-file SQLite coverage for migration discovery/application, unique verifier persistence, proof that the issued raw refresh value is absent from every persisted session column and only its 32-byte digest is stored, and revoked-family persistence across a recreated Auth API factory in `tests/Authentication.IntegrationTests/Scenarios/RenewableSessionPersistenceTests.cs`.

**Checkpoint**: Session state is owned by the existing Authentication database, starts through the
existing initializer, survives restart, and has no consumer-API dependency.

---

## Phase 3: User Story 1 - Renewable Login Session (Priority: P1) 🎯 MVP

**Goal**: A successful enabled, non-locked login retains the existing access-token body and creates
one persistent renewable family whose raw credential exists only in a restrictive browser cookie.

**Independent Test**: A valid login returns the unchanged access-token body plus one
`auth_refresh` cookie, while invalid, disabled, and locked logins create no family or cookie.

### Tests for User Story 1

- [ ] T015 [P] [US1] Add consolidated login-session integration scenarios for successful family creation, unchanged access-token JSON shape, restrictive cookie attributes, and no session on failed/disabled/locked login in `tests/Authentication.IntegrationTests/Scenarios/RenewableLoginSessionTests.cs`.
- [ ] T016 [P] [US1] Add integration assertions that `RefreshSession:LifetimeDays` defaults to seven, rejects non-positive or overflow-producing values, requires a syntactically valid absolute frontend origin, fails fast without disclosing configured values, and gives every login-issued family/credential the same absolute UTC expiry in `tests/Authentication.IntegrationTests/Scenarios/RefreshSessionConfigurationTests.cs`.

### Implementation for User Story 1

- [ ] T017 [US1] Implement the focused Auth-owned session issuance operation that creates a family and initial credential atomically before a successful response in `src/Authentication.Infrastructure/Sessions/RenewableSessionStore.cs`.
- [ ] T018 [US1] Extend successful-login orchestration in `src/Authentication.Application/Features/Login/LoginHandler.cs` to request session issuance only after existing credential, enabled, and lockout checks pass, and extend `src/Authentication.Application/Features/Login/LoginOutcome.cs` to carry the issued opaque refresh value and absolute expiry to the API boundary without referencing HTTP, cookies, or ASP.NET Core types.
- [ ] T019 [US1] Add browser-cookie creation with `auth_refresh`, `Path=/api/auth`, no Domain, `HttpOnly`, `SameSite=Strict`, absolute family expiry, and Production-only Secure behavior in `src/Authentication.Api/Features/Sessions/RefreshCookieWriter.cs`.
- [ ] T020 [US1] Extend the login HTTP boundary to emit the refresh cookie while retaining the established `{ accessToken, expiresAtUtc }` body and generic credential failures in `src/Authentication.Api/Features/Login/LoginEndpoint.cs`.

**Checkpoint**: US1 is independently demonstrable without refresh, logout, or administrative
revocation endpoints.

---

## Phase 4: User Story 2 - Refresh Token Rotation (Priority: P1)

**Goal**: An enabled, non-locked user can exchange a current browser credential for a new access
token and one replacement credential in the same, non-extended family.

**Independent Test**: With a cookie from US1 and the exact configured Origin, refresh returns 200,
a replacement cookie, and the existing access-token response; expired, unknown, malformed, revoked,
disabled, and locked cases all return indistinguishable 401 responses without cookies.

### Tests for User Story 2

- [ ] T021 [P] [US2] Add refresh integration scenarios for valid rotation without an `Authorization` header, an irrelevant/expired Bearer not being required for success, same-family fixed absolute expiry, current role claims, rejection of unknown/malformed/expired/revoked credentials, and persistence unavailability producing generic 503 ProblemDetails with no replacement cookie in `tests/Authentication.IntegrationTests/Scenarios/RefreshRotationTests.cs`.
- [ ] T022 [P] [US2] Add deterministic `ControlledTimeProvider` scenarios proving expired, disabled, and Identity-locked users receive the same generic 401 refresh contract with no replacement cookie in `tests/Authentication.IntegrationTests/Scenarios/RefreshCredentialFailureTests.cs`.
- [ ] T023 [P] [US2] Add Origin-boundary integration scenarios proving missing, malformed, opaque, and mismatched Origin values return 403 before credential processing and the exact configured origin succeeds in `tests/Authentication.IntegrationTests/Scenarios/RefreshOriginProtectionTests.cs`.

### Implementation for User Story 2

- [ ] T024 [US2] Add refresh orchestration under `src/Authentication.Application/Features/Sessions/`: keep `RefreshSessionHandler` in `RefreshSessionHandler.cs`, and its command and outcome as one top-level type per matching file; preserve generic unusable-credential outcomes and issue existing access-token claims only after successful rotation.
- [ ] T025 [US2] Implement serialized SQLite refresh consumption: verify family, credential, and current Identity state; consume exactly one current credential; create exactly one same-family replacement; and commit before return in `src/Authentication.Infrastructure/Sessions/RenewableSessionStore.cs`.
- [ ] T026 [US2] Add exact configured-Origin validation with generic 403 failure and no CORS enablement in `src/Authentication.Api/Features/Sessions/BrowserOriginValidator.cs`.
- [ ] T027 [US2] Add anonymous `POST /api/auth/refresh` mapping that requires no access JWT, validates Origin before cookie processing, maps every unusable credential state to generic 401 ProblemDetails, maps persistence unavailability to generic 503 ProblemDetails, and emits the replacement cookie and existing access-token response only on success in `src/Authentication.Api/Features/Sessions/RefreshEndpoint.cs`.
- [ ] T028 [US2] Register the refresh-session endpoint group and its API-boundary collaborators in `src/Authentication.Api/Program.cs`.

**Checkpoint**: US2 provides the normal renewable-session path without exposing refresh material in
JSON or extending family expiry.

---

## Phase 5: User Story 3 - Replay Detection Protects a Session Family (Priority: P1)

**Goal**: Reuse of a consumed credential terminates the entire family, and competing requests never
produce two valid continuations.

**Independent Test**: Rotate once, replay the original cookie, then prove the replacement fails;
coordinate two requests for one current cookie and prove at most one succeeds.

### Tests for User Story 3

- [ ] T029 [P] [US3] Add real-SQLite integration coverage that replaying a consumed credential returns generic 401, records a replay event without secrets, and prevents its replacement from refreshing in `tests/Authentication.IntegrationTests/Scenarios/RefreshReplayDetectionTests.cs`.
- [ ] T030 [P] [US3] Add coordinated concurrent HTTP refresh coverage proving two requests using one credential yield at most one 200 and no independent valid continuation in `tests/Authentication.IntegrationTests/Scenarios/RefreshConcurrencyTests.cs`.

### Implementation for User Story 3

- [ ] T031 [US3] Extend serialized refresh handling so a consumed credential presentation stamps the owning family with the irreversible `Replay` reason before returning the generic invalid-refresh outcome in `src/Authentication.Infrastructure/Sessions/RenewableSessionStore.cs`.
- [ ] T032 [US3] Add structured replay-detection logging with UTC time, family/user identifiers, and correlation context while excluding cookies, raw tokens, hashes, access tokens, keys, and configuration secrets in `src/Authentication.Infrastructure/Sessions/RenewableSessionStore.cs`.

**Checkpoint**: Replay containment and one-consumer concurrency semantics are observable and
verified with real SQLite transactions.

---

## Phase 6: User Story 4 - Logout Ends the Current Renewable Session (Priority: P2)

**Goal**: A browser can idempotently revoke the family represented by its cookie and clear that
cookie without centrally revoking any access JWT.

**Independent Test**: Logout returns 204 and clears the matching cookie for active, absent,
malformed, unknown, expired, and already-revoked cookies; an active-family cookie cannot refresh
after logout.

### Tests for User Story 4

- [ ] T033 [P] [US4] Add logout integration scenarios for family revocation, matching expired cookie attributes, idempotent 204 behavior for all credential states, refresh rejection after logout, and persistence unavailability producing generic 503 ProblemDetails rather than false success in `tests/Authentication.IntegrationTests/Scenarios/LogoutTests.cs`.
- [ ] T034 [P] [US4] Add logout Origin-protection scenarios for missing, malformed, opaque, mismatched, and configured Origin values in `tests/Authentication.IntegrationTests/Scenarios/LogoutOriginProtectionTests.cs`.

### Implementation for User Story 4

- [ ] T035 [US4] Add focused idempotent logout orchestration under `src/Authentication.Application/Features/Sessions/`, keeping `LogoutSessionHandler` and any command/outcome as one top-level type per matching file; revoke a known family with reason `Logout` while treating absent, malformed, unknown, expired, or already-revoked credentials as success.
- [ ] T036 [US4] Implement known-family logout revocation and secret-free structured logout event logging with UTC event time and ambient `Activity` trace/span/correlation identifiers when available in `src/Authentication.Infrastructure/Sessions/RenewableSessionStore.cs`.
- [ ] T037 [US4] Add matching expired `auth_refresh` cookie invalidation in `src/Authentication.Api/Features/Sessions/RefreshCookieWriter.cs`.
- [ ] T038 [US4] Add `POST /api/auth/logout` mapping that validates Origin first, returns 204 and clears the cookie for every credential state, maps actual persistence unavailability to generic 503 ProblemDetails, and does not create JWT blacklist state in `src/Authentication.Api/Features/Sessions/LogoutEndpoint.cs`.

**Checkpoint**: US4 ends future renewal for the current family while already-issued access JWTs
remain handled exclusively by normal local validation and expiry.

---

## Phase 7: User Story 5 - Administrators Revoke User Sessions (Priority: P2)

**Goal**: An Administrator can revoke every active renewable family for one existing user without
session listing or consumer-API session access.

**Independent Test**: Create two families for one user, revoke them with an Administrator JWT, and
prove neither refreshes; verify established 401, 403, and 404 outcomes.

### Tests for User Story 5

- [ ] T039 [P] [US5] Add administrative all-family revocation integration scenarios for two independent login families and subsequent refresh rejection in `tests/Authentication.IntegrationTests/Scenarios/AdministrativeSessionRevocationTests.cs`.
- [ ] T040 [P] [US5] Add integration assertions for administrator endpoint 401, 403, unknown-user 404, and persistence-unavailable 503 ProblemDetails conventions in `tests/Authentication.IntegrationTests/Scenarios/AdministrativeSessionRevocationAccessTests.cs`.

### Implementation for User Story 5

- [ ] T041 [US5] Extend the existing user-administration application contract with all-family session revocation and existing-user outcome handling in `src/Authentication.Application/Features/Users/IUserAdministration.cs`.
- [ ] T042 [US5] Implement atomic active-family lookup/revocation with reason `Administrator` and secret-free structured event logging with UTC event time and ambient `Activity` trace/span/correlation identifiers when available in `src/Authentication.Infrastructure/Identity/UserAdministration.cs`.
- [ ] T043 [US5] Add the Administrator-protected `POST /api/admin/users/{id}/revoke-sessions` route preserving existing 401, 403, 404, and persistence-unavailable 503 ProblemDetails conventions in `src/Authentication.Api/Features/Users/UserAdministrationEndpoints.cs`.

**Checkpoint**: US5 allows containment of all a user’s renewable families without changing consumer
JWT validation or adding session-management interfaces.

---

## Phase 8: User Story 6 - Disabling an Account Ends Renewable Sessions (Priority: P2)

**Goal**: Accepted account disablement atomically revokes every active family; later enablement does
not reactivate any session, and refusal to disable the last enabled Administrator leaves sessions
unchanged.

**Independent Test**: Disable a user with multiple families, prove all refreshes fail, re-enable and
prove old cookies remain unusable; verify a refused last-Administrator disable preserves its family.

### Tests for User Story 6

- [ ] T044 [P] [US6] Add integration coverage for accepted disablement revoking all families, enablement not restoring them, and a new post-enable login creating only a new family in `tests/Authentication.IntegrationTests/Scenarios/UserDisableSessionRevocationTests.cs`.
- [ ] T045 [P] [US6] Extend last-enabled-Administrator continuity coverage to prove a refused disable leaves the account and its renewable family unchanged in `tests/Authentication.IntegrationTests/Scenarios/AdministratorContinuityTests.cs`.

### Implementation for User Story 6

- [ ] T046 [US6] Extend accepted disablement so it changes `IsEnabled` and revokes every active family with reason `UserDisabled` in the same serializable operation, while enablement changes no family, in `src/Authentication.Infrastructure/Identity/UserAdministration.cs`.
- [ ] T047 [US6] Add secret-free disablement session-revocation event logging with UTC event time and ambient `Activity` trace/span/correlation identifiers when available, only after a successful disable transition, in `src/Authentication.Infrastructure/Identity/UserAdministration.cs`.

**Checkpoint**: US6 completes the Phase 3 disablement extension without weakening the
last-enabled-Administrator protection.

---

## Phase 9: Gate G4 Verification and Cross-Cutting Evidence

**Purpose**: Demonstrate the completed vertical slice, persistence lifecycle, secret-safe logs,
local consumer validation, and all Phase 1–3 regression behavior.

- [ ] T048 [P] Add a Phase 4 Compose acceptance lifecycle covering login, rotation, replay, concurrent refresh, logout, administrative revocation, disable/enable, restart persistence, and Phase 1–3 regression invocation in `tests/acceptance/phase-4.sh`.
- [ ] T049 Extend the acceptance log scan for passwords, access tokens, raw refresh cookies, token hashes, and `PRIVATE KEY`, while asserting replay/logout/administrator/disablement revocation events are present with UTC timestamps and correlation identifiers when the requests expose them, in `tests/acceptance/phase-4.sh`.
- [ ] T050 [P] Add consumer integration evidence that APIs A and B accept a pre-revocation unexpired JWT locally after logout, replay, disablement, and administrative revocation in `tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs`.
- [ ] T051 Run build and the complete unit/integration suite, resolve Phase 4 and Phase 1–3 regressions, and create `docs/phase-4-operations.md` recording exact commands, environment/configuration prerequisites, and PASS evidence while keeping `specs/004-phase-4-refresh-sessions-logout/quickstart.md` as a reusable validation guide.
- [ ] T052 Run the disposable Phase 4 acceptance script and record all five Gate G4 evidence states—build, tests, startup, feature, and regression—in `docs/phase-4-operations.md`, linked to the focused test and acceptance output.
- [ ] T053 After T001-T052, review implementation and evidence against Roadmap §10.10, present `docs/phase-4-operations.md` to the project owner, and obtain explicit Gate G4 approval. Only after that approval, update the authorized Phase 4 checklist/status/progress records in `baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md` and `specs/004-phase-4-refresh-sessions-logout/checklists/requirements.md`, record the approval date/evidence reference without changing normative requirements, and create the identifiable Gate G4 closing commit; never mark Phase 4 complete or create the closing commit before approval.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** has no implementation dependencies.
- **Phase 2** depends on Phase 1 and blocks all story phases.
- **T006** depends on T004 and **T007** depends on T005; after those model types exist, the two test tasks may run in parallel.
- **US1** depends on Phase 2.
- **US2** depends on US1 because it consumes the login-issued credential.
- **US3** depends on US2 because replay is defined from a successful rotation.
- **US4** depends on US2 because logout revokes a browser family; it may proceed in parallel with US3 once US2 completes.
- **US5** depends on US1 and Phase 2; it may proceed in parallel with US3/US4 once its shared family state exists.
- **US6** depends on US5’s all-family revocation capability and existing Phase 3 continuity behavior.
- **Gate G4 verification** depends on all story phases.

### User Story Completion Order

```text
Setup → Foundation → US1 → US2 → ┬→ US3 →┐
                                  ├→ US4 →┼→ Gate G4
                                  └→ US5 → US6 ┘
```

### Parallel Opportunities

- T002/T003, T006/T007, T015/T016, T021/T023, T029/T030, T033/T034, T039/T040, T044/T045,
  and T048/T050 can run in parallel once their prerequisites are satisfied.
- US3, US4, and US5 affect separate story-boundary files after US2; coordinate the shared
  `RenewableSessionStore.cs` changes before merging.

## Parallel Example: User Story 2

```text
Task: "Add rotation and generic-failure scenarios in tests/Authentication.IntegrationTests/Scenarios/RefreshRotationTests.cs"
Task: "Add deterministic expiry/disabled/locked scenarios in tests/Authentication.IntegrationTests/Scenarios/RefreshCredentialFailureTests.cs"
Task: "Add Origin-protection scenarios in tests/Authentication.IntegrationTests/Scenarios/RefreshOriginProtectionTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete setup and foundational state.
2. Complete US1 and prove secure login-created session persistence.
3. Complete US2 and prove the basic renewal path with rotation.
4. Validate each focused scenario before adding replay containment, logout, or administration.

### Incremental Delivery

1. US1 → renewable login session.
2. US2 → renewable access-token lifecycle.
3. US3 → replay containment.
4. US4 → user-initiated session end.
5. US5 and US6 → administrative and account-state containment.
6. Gate G4 → Compose lifecycle plus Phase 1–3 regressions.

## Notes

- General refresh rate limiting remains deferred to Phase 7 per the recorded clarification; do not
  add it to these tasks.
- Keep raw refresh values, hashes, access tokens, passwords, keys, and configuration secrets out
  of logs, contracts, and assertions.
- Do not create `plan.md` changes, frontend work, a second database, a migration container,
  centralized JWT validation, or future password-workflow components.
