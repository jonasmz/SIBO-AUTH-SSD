---
description: "Executable task list for Phase 2 — JWT Validation in Consumer APIs"
---

# Tasks: Phase 2 — JWT Validation in Consumer APIs

**Input**: Design documents in specs/002-jwt-validation-consumer-apis/

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/reference-consumer-api.openapi.yaml, and quickstart.md

**Tests**: Tests are mandatory for current critical behavior (spec NFR-002). Extend the existing tests/Authentication.IntegrationTests project with the four consolidated scenarios in the plan, using real JwtBearer validation of real RS256 tokens, `WebApplicationFactory`, and disposable RSA pairs. Do not add a test project or a mocking library, do not use sleeps, and do not test future phases.

**Organization**: Tasks are ordered by the three user stories. Complete validation (signature, algorithm, issuer, audience, lifetime, tolerance) is built in the foundational phase so the consumer is never run with partial validation; US1 proves acceptance and availability independence, US2 proves and hardens rejection, and US3 adds role-based authorization.

## Format: [ID] [P?] [Story] Description

- [P] means the task can run in parallel after its stated prerequisites because it changes different files.
- [US#] maps a task to its feature user story.
- Every task names implementation, test, configuration, or documentation paths.
- Authentication API source under src/Authentication.* MUST remain unchanged in this phase.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the single new package and the reference consumer project without feature behavior.

- [ ] T001 Add `Microsoft.AspNetCore.Authentication.JwtBearer` at the concrete version 10.0.12 (no floating range) to Directory.Packages.props; add no other package.
- [ ] T002 Create src/ReferenceConsumer.Api/ReferenceConsumer.Api.csproj (Microsoft.NET.Sdk.Web, central package versions, JwtBearer reference only) with no ProjectReference to any Authentication.* project, add it to Authentication.slnx, and add a ProjectReference to it from tests/Authentication.IntegrationTests/Authentication.IntegrationTests.csproj.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Supply configuration, complete token validation, composition, and test plumbing needed by every story. No story endpoint is completed in this phase.

**⚠️ CRITICAL**: Complete this phase before starting user-story implementation.

- [ ] T003 Define and validate consumer configuration in src/ReferenceConsumer.Api/Security/ConsumerJwtOptions.cs and src/ReferenceConsumer.Api/appsettings.json: `Service:Name` required nonblank; `Jwt:Issuer`, `Jwt:Audience` required nonblank; `Jwt:PublicKeyPath` required readable file; `Jwt:ClockSkewSeconds` required integer "0–60" inclusive with the reference value 30 supplied by deployment, not defaulted in code; any missing or invalid value terminates startup with a message naming only the setting, never its value or path.
- [ ] T004 Implement local JWT validation in src/ReferenceConsumer.Api/Security/JwtValidationRegistration.cs: load the public key from the configured PEM and reject any PEM that is not labelled `PUBLIC KEY` or `RSA PUBLIC KEY` (so a private key fails startup naming `Jwt:PublicKeyPath`); register JwtBearer as the default scheme with explicit `TokenValidationParameters` (`IssuerSigningKey` from the key, `ValidAlgorithms` = RS256 only, `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey`, `RequireSignedTokens`, `RequireExpirationTime` all true, `ClockSkew` from configuration); set no Authority or MetadataAddress; `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`, `IncludeErrorDetails = false`; add the authorization policy named `Administrator` requiring the role `Administrator`; use only Microsoft IdentityModel for validation and never log tokens or key material.
- [ ] T005 Create the composition root in src/ReferenceConsumer.Api/Program.cs and src/ReferenceConsumer.Api/ReferenceConsumerEntryPoint.cs: native DI, options validation, authentication and authorization middleware, safe production error handling, and endpoint mounting; keep logic out of Program.cs; expose the public `ReferenceConsumerEntryPoint` marker type for `WebApplicationFactory`.
- [ ] T006 [P] Implement anonymous `GET /health/live` returning exactly `{ "status": "healthy" }` in src/ReferenceConsumer.Api/Features/Health/LivenessEndpoint.cs and mount it from src/ReferenceConsumer.Api/Program.cs.
- [ ] T007 [P] Create test plumbing in tests/Authentication.IntegrationTests/Infrastructure/ReferenceConsumerFactory.cs and tests/Authentication.IntegrationTests/Infrastructure/TestTokenMinter.cs: a factory hosting the consumer through `ReferenceConsumerEntryPoint` with externally supplied configuration (service name, issuer, audience, public key file, clock skew) that can run as `api-a` and `api-b`, and a minter that signs tokens with `JsonWebTokenHandler` and a disposable RSA pair with controllable algorithm, issuer, audience, roles, `iat`, and `exp`; do not use mocks, sleeps, or tracked secrets.

**Checkpoint**: Solution builds, the consumer starts only with valid configuration and a public-key PEM, and integration tests can host both consumer instances and mint tokens.

---

## Phase 3: User Story 1 - Business APIs Accept Valid Tokens Locally (Priority: P1) 🎯 MVP

**Goal**: A token issued by Authentication API is accepted by a protected endpoint in both business APIs, which identify the caller by stable user identifier and roles without contacting Authentication API, using only a read-only public key.

**Independent Test**: Log in through Authentication API, call `GET /api/caller` on `api-a` and `api-b` with the token, and confirm identical `subject` and roles; stop Authentication API and confirm the still-valid token is accepted; confirm no private key reaches either consumer.

### Tests for User Story 1

- [ ] T008 [US1] Write the failing consolidated scenarios in tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs: (a) a real Authentication API factory and two consumer instances share one RSA pair, a logged-in administrator token is accepted by both on `/api/caller` with the same `subject` (`7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02`), `roles` `["Administrator"]`, and each instance's own `service` name, and both still accept it after the Authentication API host is disposed; (b) startup terminates, naming only the offending setting and leaking no key material or path, for missing issuer, missing audience, out-of-range or non-numeric clock skew, missing public-key file, and a private-key PEM supplied as the public key.

### Implementation for User Story 1

- [ ] T009 [US1] Implement authenticated `GET /api/caller` returning `{ service, subject, roles }` (all `role` claim values, empty when none) in src/ReferenceConsumer.Api/Features/Caller/CallerIdentityResponse.cs and src/ReferenceConsumer.Api/Features/Caller/CallerEndpoints.cs, mounted from src/ReferenceConsumer.Api/Program.cs per specs/002-jwt-validation-consumer-apis/contracts/reference-consumer-api.openapi.yaml.
- [ ] T010 [P] [US1] Create the non-root multi-stage image in src/ReferenceConsumer.Api/Dockerfile (authorized `sdk:10.0`/`aspnet:10.0` development tags, only consumer project and shared build files copied, no secrets) and add `api-a` and `api-b` to compose.yml: one build for both, `Service__Name`, shared `Jwt__Issuer`/`Jwt__Audience` from `AUTH_JWT_ISSUER`/`AUTH_JWT_AUDIENCE`, shared `Jwt__ClockSkewSeconds` from `AUTH_JWT_CLOCK_SKEW_SECONDS`, `Jwt__PublicKeyPath` pointing at a single read-only bind-mounted file from `AUTH_JWT_PUBLIC_KEY_HOST_FILE`, per-service host ports `API_A_HTTP_PORT`/`API_B_HTTP_PORT`; mount neither `AUTH_RSA_HOST_PATH` nor the SQLite directory; add no proxy, frontend, migration, bootstrap, or other service and no named volume.
- [ ] T011 [P] [US1] Extend .env.example and add docs/phase-2-operations.md with the new variables (`AUTH_JWT_PUBLIC_KEY_HOST_FILE`, `AUTH_JWT_CLOCK_SKEW_SECONDS`, `API_A_HTTP_PORT`, `API_B_HTTP_PORT`), how to derive the public key from the Phase 1 private key (`openssl pkey -pubout`), that only the public file is mounted into consumers, and that publishing consumer ports is a development/acceptance convenience while the production no-direct-exposure rule and proxy remain Phase 8.
- [ ] T012 [US1] Make tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs scenarios (a) and (b) pass against the consumer, verifying external responses rather than mocked substitutes.
- [ ] T013 [US1] Create the disposable Compose demonstration tests/acceptance/phase-2.sh, reusing the tests/acceptance/phase-1.sh storage preparation: start `auth-api`, `api-a`, and `api-b` on disposable external storage with the public key file; obtain a real token; confirm `200` with identical `subject` and `roles` and the respective `service` on both consumers; `docker compose stop auth-api` and confirm both still accept the token; confirm via mount inspection and file search that no private key is present in either consumer; finish by running tests/acceptance/phase-1.sh as regression.

**Checkpoint**: Both consumers validate Authentication API tokens locally, keep working with Authentication API stopped, and have no access to the private key.

---

## Phase 4: User Story 2 - Business APIs Reject Invalid Tokens (Priority: P2)

**Goal**: Absent, malformed, forged, unsigned or wrong-algorithm, expired, wrong-issuer, and wrong-audience tokens are answered `401 Unauthorized` by both consumers with no body and no token, key, or validation detail.

**Independent Test**: For each rejection case, call `GET /api/caller` on both instances and confirm `401`, an empty body, and a `WWW-Authenticate` header of exactly `Bearer` without error description.

### Tests for User Story 2

- [ ] T014 [US2] Write the rejection scenarios in tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs, run against both instance configurations with minted tokens: no `Authorization` header, non-bearer scheme, malformed token text, signature from an unrelated key, `alg: none` unsigned token, a non-RS256 algorithm, expired well beyond the configured tolerance, wrong issuer, and wrong audience; each must return `401` with an empty body and `WWW-Authenticate: Bearer` carrying no `error` or `error_description`; add one case showing a token expired within the tolerance is accepted and one just beyond it is rejected, using offsets large enough to need no sleeps (or a controlled `TimeProvider` if the handler honors it).

### Implementation for User Story 2

- [ ] T015 [US2] Ensure rejection hygiene in src/ReferenceConsumer.Api/Security/JwtValidationRegistration.cs and src/ReferenceConsumer.Api/Program.cs: authentication failures return `401` with `WWW-Authenticate: Bearer` only (no error description, no body) and unexpected errors yield safe generic output without stack traces or token/key detail; adjust only if T014 shows deviation.
- [ ] T016 [US2] Make the T014 rejection scenarios pass for both `api-a` and `api-b` configurations without weakening the validation parameters from T004.
- [ ] T017 [US2] Extend tests/acceptance/phase-2.sh to confirm over Compose that a request without a token and a request with a tampered token each return `401` with no body from both consumers.

**Checkpoint**: Every tested invalid-token case is rejected by both consumers without leaking detail; US1 acceptance still passes.

---

## Phase 5: User Story 3 - Role-Based Access Is Enforced (Priority: P3)

**Goal**: A valid token carrying the `Administrator` role is accepted on the role-restricted endpoint; a valid token lacking it receives `403 Forbidden`; no valid token receives `401`.

**Independent Test**: Call `GET /api/caller/administrator` on both instances with an `Administrator` token (`200`), a validly signed token with a different role or no role (`403`), and no token (`401`).

### Tests for User Story 3

- [ ] T018 [US3] Write the authorization scenarios in tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs for both instances: a minted `Administrator` token (single role and multiple roles including `Administrator`) → `200` on `/api/caller/administrator` with the full role set; a validly signed token with role `Operator` and one with no role claim → `200` on `/api/caller` and `403` with an empty body on `/api/caller/administrator`; no token → `401` (never `403`).

### Implementation for User Story 3

- [ ] T019 [US3] Add authenticated `GET /api/caller/administrator` protected by the `Administrator` policy, returning the same `{ service, subject, roles }` shape, in src/ReferenceConsumer.Api/Features/Caller/CallerEndpoints.cs per the OpenAPI contract; forbidden responses carry no body.
- [ ] T020 [US3] Make the T018 authorization scenarios pass and extend tests/acceptance/phase-2.sh to confirm a real administrator token receives `200` on `/api/caller/administrator` from both consumers (the `403` case is proven by the automated suite because Phase 2 cannot issue a real non-administrator token).

**Checkpoint**: `401` and `403` are distinct, role claims drive authorization, and US1/US2 behavior is intact.

---

## Phase 6: Polish & Gate G2 Evidence

**Purpose**: Complete only Phase 2 documentation, regression, security review, and G2 evidence.

- [ ] T021 [P] Reconcile configuration and operational instructions with delivered behavior in .env.example, docs/phase-2-operations.md, and specs/002-jwt-validation-consumer-apis/quickstart.md; exclude Phase 3–8 workflows.
- [ ] T022 Run `dotnet build Authentication.slnx --no-incremental` and `dotnet test --solution Authentication.slnx`, correcting all first-party compiler/analyzer warnings and current-phase or Phase 1 regressions in the relevant src/ or tests/ file rather than suppressing them globally.
- [ ] T023 Run tests/acceptance/phase-2.sh and the quickstart.md G2 procedure against disposable external storage; record build, tests, local validation on both consumers, rejection, role authorization, Authentication-API-stopped acceptance, private-key absence, and Phase 1 regression evidence in docs/phase-2-operations.md without modifying normative files under baseline/.
- [ ] T024 Inspect src/, compose.yml, .env.example, src/ReferenceConsumer.Api/Dockerfile, and tests/ for Phase 2 governance compliance: `git diff` shows src/Authentication.* unchanged; `Microsoft.AspNetCore.Authentication.JwtBearer` is the only new package; the consumer references no Authentication.* project, has no Authority/MetadataAddress/JWKS/introspection, persistence, or secrets; the private key and SQLite directory are mounted only into `auth-api`; Compose contains no proxy, frontend, migration, or bootstrap service and no named volume; no refresh, administration, revocation, or other later-phase behavior exists.
- [ ] T025 Only after T001-T024 are complete, verify every applicable Roadmap section 8.5 Gate G2 acceptance criterion and confirm build, tests, local validation, Authentication-API-unavailable acceptance, and Phase 1 regression pass; present the evidence and obtain explicit approval from the project owner; only then record the Gate G2 approval, update the Phase 2 status and progress records in baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md (including the dashboard, section 8.5 criteria, progress log, and current state), record DEC-009 in the roadmap decision log for the project-owner decision to add the `ReferenceConsumer.Api` project as the deployable Business API A/B host outside Technical Constraints section 5.2, update the Gate G2 closure section in specs/002-jwt-validation-consumer-apis/checklists/requirements.md with evidence references, and create the identifiable Phase 2 closing commit. These explicit, reviewed, version-controlled status/checklist/progress/decision updates are permitted and required for closure; do not mark Phase 2 complete before verification and approval, and do not alter normative requirements, technical constraints, architecture, scope, or implementation sequencing through a tracking update.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on T001-T002 and blocks every user story.
- **US1 (Phase 3)**: Depends on T003-T007 and produces the first running consumers.
- **US2 (Phase 4)**: Depends on US1, especially T009, T012, and T013 (it exercises the same endpoint and test file).
- **US3 (Phase 5)**: Depends on US1 (and the `Administrator` policy from T004); extends the same endpoint file and test class as US1/US2.
- **Polish/G2 (Phase 6)**: Depends on US1, US2, and US3 completion.

### User Story Dependencies

- **US1 (P1)**: First independently executable vertical slice; no feature-story dependency.
- **US2 (P2)**: Verifies and hardens the rejection behavior of the US1 endpoint; adds no new service or persistence.
- **US3 (P3)**: Adds one role-restricted endpoint and its scenarios; adds no role management.

### Parallel Opportunities

- T006 and T007 can proceed in parallel after T005, because they touch different files.
- T010 and T011 can proceed in parallel after T009, because Dockerfile/Compose and documentation are independent files.
- T021 can proceed in parallel with final code review once configuration paths are stable.
- Tasks marked [P] touch distinct files; all other tasks preserve listed dependencies. The three story test tasks share one test file and therefore run sequentially.

## Parallel Example: Foundational Phase

Task: Implement anonymous liveness in src/ReferenceConsumer.Api/Features/Health/LivenessEndpoint.cs

Task: Create ReferenceConsumerFactory and TestTokenMinter in tests/Authentication.IntegrationTests/Infrastructure/

## Implementation Strategy

### MVP First (US1)

1. Complete Setup and Foundational work.
2. Complete US1 through T013.
3. Start from empty disposable external storage and validate local acceptance, Authentication-API-stopped acceptance, and private-key absence.
4. Do not claim rejection or role-authorization completion until later story checkpoints pass.

### Incremental Delivery

1. US1 produces two running consumers that validate and accept real tokens locally.
2. US2 proves and hardens rejection of every invalid-token case.
3. US3 adds role-restricted access with distinct `401`/`403`.
4. Polish records Gate G2 evidence; implementation stops at the Phase 2 boundary.

## Notes

- All tasks use current-phase behavior only; no task authorizes refresh tokens, sessions, revocation, introspection, JWKS, administration endpoints, password flows, rate limiting, reverse proxy, frontend, or final topology work.
- The consumer is intentionally a single minimal project outside the four-project Authentication layout; this is the project-owner decision recorded in plan.md Complexity Tracking and to be logged as DEC-009 in T025.
- Authentication API code, contracts, and persistence are unchanged; the shared audience is the existing single `aud` value.
- A task is not complete merely because its checkbox is checked: it must meet its traced requirement, preserve existing tests, and satisfy its phase checkpoint.
