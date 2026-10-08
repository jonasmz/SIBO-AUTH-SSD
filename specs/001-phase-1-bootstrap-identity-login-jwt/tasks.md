---
description: "Executable task list for Phase 1 — Bootstrap, Identity, Admin, Login and JWT"
---

# Tasks: Phase 1 — Bootstrap, Identity, Admin, Login and JWT

**Input**: Design documents in specs/001-phase-1-bootstrap-identity-login-jwt/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/authentication-api.openapi.yaml, and quickstart.md

**Tests**: Tests are mandatory for current critical behavior. Use the four consolidated integration scenarios in the plan, xUnit.net v3, WebApplicationFactory, real SQLite, controlled TimeProvider, and disposable RSA test material. Do not test future phases.

**Organization**: Tasks are ordered by the three user stories. US2 relies on the ready service from US1; US3 extends that slice with lifecycle persistence evidence.

## Format: [ID] [P?] [Story] Description

- [P] means the task can run in parallel after its stated prerequisites because it changes different files.
- [US#] maps a task to its feature user story.
- Every task names implementation, test, configuration, or documentation paths.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the mandated .NET 10 solution, projects, package management, and test host without feature behavior.

- [X] T001 Create global.json, Directory.Build.props, .editorconfig, and Directory.Packages.props with .NET 10, stable C# 14, net10.0, Nullable=enable, ImplicitUsings=enable, EnforceCodeStyleInBuild=true, and concrete centrally managed package versions.
- [X] T002 Create Authentication.slnx and src/Authentication.Domain/Authentication.Domain.csproj, src/Authentication.Application/Authentication.Application.csproj, src/Authentication.Infrastructure/Authentication.Infrastructure.csproj, and src/Authentication.Api/Authentication.Api.csproj; add only Domain ← Application ← Infrastructure ← Api references allowed by the constitution.
- [X] T003 Configure immediate Phase 1 package references in Directory.Packages.props, src/Authentication.Domain/Authentication.Domain.csproj, src/Authentication.Application/Authentication.Application.csproj, src/Authentication.Infrastructure/Authentication.Infrastructure.csproj, and src/Authentication.Api/Authentication.Api.csproj: compatible ASP.NET Core Identity/EF Core 10.x, official Microsoft.EntityFrameworkCore.Sqlite, development-only EF design tooling, and Microsoft IdentityModel signing support; do not add prohibited or later-phase packages.
- [X] T004 Create tests/Authentication.UnitTests/Authentication.UnitTests.csproj and tests/Authentication.IntegrationTests/Authentication.IntegrationTests.csproj, add them to Authentication.slnx, and configure xUnit.net v3, Microsoft Testing Platform, and Microsoft.AspNetCore.Mvc.Testing 10.x in applicable test project files.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Supply shared configuration, composition, and integration-test plumbing needed by all Phase 1 slices. No endpoint behavior is completed in this phase.

**⚠️ CRITICAL**: Complete this phase before starting user-story implementation.

- [X] T005 Define externally bound SQLite and JWT configuration in src/Authentication.Infrastructure/Security/JwtOptions.cs and src/Authentication.Api/appsettings.json: issuer, audience, configurable lifetime with a 15-minute default, database path, and private PEM path; reject missing, invalid, or unreadable required values at startup without logging secrets.
- [X] T006 Create DI registration and initialization state in src/Authentication.Infrastructure/DependencyInjection.cs and src/Authentication.Infrastructure/Persistence/InitializationState.cs so Infrastructure registers only current Phase 1 Identity, SQLite, health, and JWT services.
- [X] T007 Create the Minimal API composition root in src/Authentication.Api/Program.cs with native DI, ProblemDetails, safe production error handling, configuration, Infrastructure registration, and feature-endpoint mounting; keep business logic out of Program.cs.
- [X] T008 Create real-SQLite, RSA-key, externally supplied configuration, and controlled-time test setup in tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs and tests/Authentication.IntegrationTests/Infrastructure/Phase1TestResources.cs; do not use EF Core InMemory, mocks, sleeps, or tracked secrets.

**Checkpoint**: Solution builds, configuration fails fast safely, and integration tests can host the API against disposable real SQLite storage.

---

## Phase 3: User Story 1 - Start a Ready Authentication Service (Priority: P1) 🎯 MVP

**Goal**: An operator starts the Phase 1 Auth API from empty external storage; it applies its own migration, creates the initial Identity state, and reports safe liveness/readiness.

**Independent Test**: Start WebApplicationFactory and Docker Compose against empty disposable host storage; confirm migrations and initial identity state, safe healthy live/ready payloads, and deterministic initialization failure that never announces readiness.

### Tests for User Story 1

- [X] T009 [US1] Write the failing consolidated startup, migration, bootstrap, and health scenario in tests/Authentication.IntegrationTests/Scenarios/BootstrapAndHealthTests.cs, covering empty file-backed SQLite startup, exactly one initial role/user, safe live/ready responses, and initialization failure that terminates startup before readiness or normal traffic without leaking secrets.

### Implementation for User Story 1

- [X] T010 [US1] Implement the Identity EF Core persistence model in src/Authentication.Infrastructure/Persistence/AuthenticationDbContext.cs using native IdentityUser<string> and IdentityRole<string>; enforce the data-model constraint that NormalizedEmail has a database-enforced unique index and keep Identity password/stamp/lockout fields framework-owned.
- [X] T011 [US1] Generate and version the initial Identity SQLite migration under src/Authentication.Infrastructure/Persistence/Migrations/, ensuring it creates only Identity storage and EF migration history, not refresh, session, recovery, or future administrative tables.
- [X] T012 [US1] Implement startup migration and first-time Identity bootstrap in src/Authentication.Infrastructure/Persistence/DatabaseInitializer.cs: apply pending migrations internally; use fixed reserved IDs and one transaction to create Administrator, then create UserName=admin / Email=admin@local.invalid / password admin through Identity and assign the role; roll back an interrupted or failed first bootstrap so a later startup can retry consistently; configure Identity's externally configurable Phase 1 password policy so the explicit initial password is permitted without custom hashing.
- [X] T013 [US1] Implement SQLite availability probing in src/Authentication.Infrastructure/Health/SqliteHealthCheck.cs and wire initialization completion/failure into readiness without exposing database, Identity, key, or configuration detail.
- [X] T014 [US1] Implement GET /health/live and GET /health/ready plus the exact { "status": "healthy" } success DTO in src/Authentication.Api/Features/Health/HealthEndpoints.cs and src/Authentication.Api/Features/Health/HealthStatusResponse.cs; return safe ProblemDetails for unavailable readiness.
- [X] T015 [US1] Invoke initialization before normal traffic and mount the health feature in src/Authentication.Api/Program.cs, ensuring migration/bootstrap configuration failures terminate startup rather than expose a ready service.
- [X] T016 [P] [US1] Create the non-root multi-stage runtime image in src/Authentication.Api/Dockerfile using the Technical Constraints-authorized official `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0` development tags, plus the Phase 1 Compose service in compose.yml; use only Auth API, pass external configuration, mount the configurable SQLite host directory, omit migration/bootstrap services and dotnet ef database update, and do not embed secrets; exact production tag/digest recording remains a production-release obligation.
- [X] T017 [P] [US1] Add the Phase 1 external configuration example and persistent-storage instructions in .env.example and docs/phase-1-operations.md, including externally supplied RSA path, SQLite bind mount, restricted permissions, and the warning that admin must be replaced after first access without implementing password change.
- [X] T018 [US1] Make tests/Authentication.IntegrationTests/Scenarios/BootstrapAndHealthTests.cs pass against the startup and health slice, including evidence that migration/bootstrap failure terminates startup before readiness, plus safe public diagnostics for a running service whose database later becomes unavailable.

**Checkpoint**: From empty disposable storage, the API starts without an external migration or bootstrap step, has initial Identity state, and provides safe liveness/readiness.

---

## Phase 4: User Story 2 - Administrator Signs In and Receives an Access Token (Priority: P2)

**Goal**: The bootstrapped administrator signs in by email and receives a verifiable, short-lived RS256 access token; invalid credentials are externally indistinguishable and accounted for by Identity.

**Independent Test**: Against the ready P1 service, submit valid and invalid email/password requests, cryptographically validate the returned token with the paired public key, and inspect the persisted Identity failure count for a known-account failure.

### Tests for User Story 2

- [X] T019 [US2] Write the failing consolidated login/JWT issuance scenario in tests/Authentication.IntegrationTests/Scenarios/LoginAndJwtTests.cs, covering success, identical generic unknown-email and wrong-password 401 ProblemDetails, Identity failed-attempt accounting, RS256 signature and claim inspection as an issuance oracle, UTC iat/exp, unique jti, stable sub, matching expiry response, and default/configured lifetime; do not introduce consumer JWT Bearer validation or clock-tolerance configuration assigned to Phase 2.

### Implementation for User Story 2

- [X] T020 [P] [US2] Create current-use Login contracts and handler in src/Authentication.Application/Features/Login/AccessToken.cs, AuthenticatedIdentity.cs, IAccessTokenIssuer.cs, IIdentityCredentialValidator.cs, LoginCommand.cs, LoginOutcome.cs, and LoginHandler.cs; model only access-token issuance and never a refresh token, cookie, session, or future port.
- [X] T021 [P] [US2] Create explicit HTTP request/response mapping in src/Authentication.Api/Features/Login/LoginRequest.cs and src/Authentication.Api/Features/Login/LoginResponse.cs matching specs/001-phase-1-bootstrap-identity-login-jwt/contracts/authentication-api.openapi.yaml, including required nonblank email/password input and only accessToken plus expiresAtUtc on success.
- [X] T022 [US2] Implement Identity-backed normalized-email credential validation in src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs; use Identity password verification and failure accounting, perform Identity-hasher-equivalent work for unknown email, and return one generic outcome for unknown, wrong-password, and locked states without custom password logic.
- [X] T023 [US2] Implement external PEM loading and RS256 issuance in src/Authentication.Infrastructure/Security/JwtAccessTokenIssuer.cs; use Microsoft IdentityModel and System.TimeProvider, emit sub, email, one role per assigned role, iss, aud, iat, exp, and random jti, and never log token or private-key material.
- [X] T024 [US2] Register Login adapters and map anonymous POST /api/auth/login in src/Authentication.Infrastructure/DependencyInjection.cs, src/Authentication.Api/Features/Login/LoginEndpoint.cs, and src/Authentication.Api/Program.cs; emit generic 401 and safe 400/503/500 ProblemDetails without exposing Identity internals.
- [X] T025 [US2] Make tests/Authentication.IntegrationTests/Scenarios/LoginAndJwtTests.cs pass using real SQLite, controlled TimeProvider, and generated disposable RSA material; verify external responses and persisted Identity behavior rather than mocked substitutes.

**Checkpoint**: The initial administrator can sign in by email/password and receive a cryptographically valid RS256 access token; invalid credential states remain externally generic.

---

## Phase 5: User Story 3 - Preserve Bootstrap State Across Lifecycle Changes (Priority: P3)

**Goal**: Restarting or recreating Auth API preserves existing Identity data and signing material, and bootstrap never duplicates or overwrites established administrative state.

**Independent Test**: Initialize file-backed storage, alter an allowed stored administrator value through the test host, restart against the same file/key, then verify the preserved value, exactly one built-in user/role, stable signing material, and scoped docker compose down -v survival.

### Tests for User Story 3

- [X] T026 [US3] Write the failing lifecycle scenario in tests/Authentication.IntegrationTests/Scenarios/BootstrapLifecycleTests.cs, covering second startup, fixed built-in user/role identity, no duplicates, preservation of an intentional existing administrator modification, and persistent SQLite/key use across host restart.

### Implementation for User Story 3

- [X] T027 [US3] Complete lifecycle-preservation behavior in src/Authentication.Infrastructure/Persistence/DatabaseInitializer.cs: after the built-in user created by T012 exists, perform no mutation of email, password, role assignments, stamps, lockout state, or any other fields; repeated initialization must neither duplicate the fixed-ID user/role nor overwrite established state.
- [X] T028 [US3] Finalize durable mount and file-permission handling in compose.yml, src/Authentication.Api/Dockerfile, and docs/phase-1-operations.md: SQLite/private PEM must be host bind mounts or explicitly external volume, the key mount read-only where supported, writable state available to the non-root process, and neither asset dependent on a Compose-managed named volume.
- [X] T029 [US3] Make tests/Authentication.IntegrationTests/Scenarios/BootstrapLifecycleTests.cs pass with temporary SQLite files and the same RSA pair, proving bootstrap preserves changed state and does not recreate or overwrite it.
- [X] T030 [US3] Create the disposable Compose lifecycle demonstration in tests/acceptance/phase-1.sh, using an explicit external temporary state directory to prove empty startup, restart, docker compose down -v survival of SQLite/private key, stable key fingerprint, and successful ready/login behavior without future services.

**Checkpoint**: Existing Phase 1 identity state and RSA material survive the required lifecycle, and repeated initialization remains safe and non-destructive.

---

## Phase 6: Polish & Gate G1 Evidence

**Purpose**: Complete only Phase 1 documentation, regression, security review, and G1 evidence.

- [X] T031 [P] Reconcile configuration and operational instructions with delivered behavior in .env.example, docs/phase-1-operations.md, and specs/001-phase-1-bootstrap-identity-login-jwt/quickstart.md; retain the explicit first-access password-replacement warning and exclude Phase 2-8 workflows.
- [X] T032 Run dotnet build Authentication.slnx and dotnet test Authentication.slnx, correcting all first-party compiler/analyzer warnings and current-phase regressions in the relevant src/ or tests/ file rather than suppressing them globally.
- [X] T033 Run tests/acceptance/phase-1.sh and the quickstart.md G1 procedure against disposable external storage; record build, tests, successful startup, failed-initialization/no-readiness, migration/bootstrap, login/JWT issuance, health, restart, and down -v evidence in docs/phase-1-operations.md without modifying normative files under baseline/.
- [X] T034 Inspect src/, compose.yml, .env.example, src/Authentication.Api/Dockerfile, and tests/ for Phase 1 governance compliance: no secrets in tracked files/logs/responses, no forbidden dependencies or EF Core InMemory persistence tests, no external migration/bootstrap process, no future endpoints/entities/ports, no runtime OpenAPI/Scalar or consumer JWT validation introduced before their roadmap phases, only the Technical Constraints-authorized official .NET 10.0 development image tags, and no unauthorized service or volume.
- [X] T035 Only after T001-T034 are complete, verify every applicable Roadmap section 7.5 Gate G1 acceptance criterion and confirm build, tests, startup, feature workflow, restart/persistence, and required current/previous regression checks pass; then record Gate G1 verification and explicit approval using existing project conventions, update the Phase 1 status and progress records in baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md, update the Gate G1 closure section in specs/001-phase-1-bootstrap-identity-login-jwt/checklists/requirements.md with evidence references, record a decision only if an actual architectural or roadmap-impacting decision occurred, and create the identifiable Phase 1 closing commit. These explicit, reviewed, version-controlled status/checklist/progress updates are permitted and required for closure; do not mark Phase 1 complete before verification succeeds, and do not alter normative requirements, technical constraints, architecture, scope, or implementation sequencing through a tracking update.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on T001-T004 and blocks every user story.
- **US1 (Phase 3)**: Depends on T005-T008 and produces the first runnable service.
- **US2 (Phase 4)**: Depends on ready service from US1, especially T015 and T018.
- **US3 (Phase 5)**: Depends on persistence/bootstrap from US1 and RSA-backed login from US2.
- **Polish/G1 (Phase 6)**: Depends on US1, US2, and US3 completion.

### User Story Dependencies

- **US1 (P1)**: First independently executable vertical slice; no feature-story dependency.
- **US2 (P2)**: Uses initialized Identity and healthy API from US1; does not introduce another database, sessions, or a consumer API.
- **US3 (P3)**: Extends the same Identity and signing assets with restart/recreation behavior; it adds no administrative mutation endpoint or future persistence.

### Parallel Opportunities

- T016 and T017 can proceed in parallel after Setup project paths exist.
- After US1 is complete, T020 and T021 can proceed in parallel within US2 because they touch
  independent Application and Api contract files.
- T031 can proceed in parallel with final code review once configuration paths are stable.
- Tasks marked [P] touch distinct files; all other tasks preserve listed dependencies.

## Parallel Example: User Story 2

Task: Create Login application contracts and handler in src/Authentication.Application/Features/Login/

Task: Create explicit Login HTTP DTOs in src/Authentication.Api/Features/Login/LoginRequest.cs and LoginResponse.cs

## Implementation Strategy

### MVP First (US1)

1. Complete Setup and Foundational work.
2. Complete US1 through T018.
3. Start from empty disposable external storage and validate migrations, bootstrap, liveness, and readiness.
4. Do not claim login, JWT, or persistence-lifecycle completion until later story checkpoints pass.

### Incremental Delivery

1. US1 produces a healthy, initialized API from empty storage.
2. US2 adds only email/password login and RS256 access-token issuance, then verifies it end-to-end.
3. US3 proves idempotent preservation and Compose-independent persistent state.
4. Polish records Gate G1 evidence; implementation stops at the Phase 1 boundary.

## Notes

- All tasks use current-phase behavior only; no task authorizes refresh tokens, sessions, JWT validation in API A/B, management endpoints, password change/recovery, SMTP, JWKS, key rotation, rate limiting, or final topology work.
- Program.cs remains a composition root. Identity/EF/SQLite/JWT implementations stay in Infrastructure; Application remains independent of Infrastructure; Domain receives no artificial Identity model.
- A task is not complete merely because its checkbox is checked: it must meet its traced requirement, preserve existing tests, and satisfy its Phase 1 checkpoint.

---

## Phase 7: Convergence

**Purpose**: Corrective work found by `/speckit-converge`; complete before the T035 Gate G1 verification so the closure evidence reflects the final code. T035 remains the governance/approval task and is not duplicated here.

- [X] T036 Make startup failure diagnostics actionable without exposing secrets in src/Authentication.Infrastructure/DependencyInjection.cs, src/Authentication.Infrastructure/Persistence/DatabaseInitializer.cs, and src/Authentication.Infrastructure/Security/JwtAccessTokenIssuer.cs: identify which required setting name (never its value) is invalid or unreadable, and log the failing initialization stage (migration or bootstrap) plus the SQLite error code, per NFR-002, NFR-DB-INIT-014, and spec Edge Cases (partial)
- [X] T037 Add a failing-then-passing configuration-failure scenario to tests/Authentication.IntegrationTests/Scenarios/BootstrapAndHealthTests.cs covering a missing private-key path and an unparsable PEM: startup terminates before readiness, the failure names the invalid setting, and neither the key path nor key material appears in the exception or logs, per plan: Verification Design scenario 4 and spec Edge Cases (partial)
