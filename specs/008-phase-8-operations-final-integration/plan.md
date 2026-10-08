# Implementation Plan: Phase 8 — Operations, Deployment Integration and Final Acceptance

**Branch**: `008-phase-8-operations-final-integration` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-phase-8-operations-final-integration/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 8, Gate G8 (Roadmap §14)

## Summary

Close the product as one deployable, operable unit without new functional capability.
(1) A minimal first-party `PersistentFileLoggerProvider` (Technical Constraints §15.3–15.6) writes
the same `ILogger` events as the console to daily UTC files through an in-memory queue and one
background writer, with 30-day configurable retention, on a host bind mount; the six missing
security events (login success, user created, user enabled/disabled, role assigned/removed) are
added beside the existing ones. (2) `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1) and
`Scalar.AspNetCore` in read-only mode are mapped only in Development, with endpoint metadata and a
Bearer security scheme; production neither maps nor routes them. (3) `compose.yml` becomes the
production topology: four services, a `frontend` built from the official Nginx image that serves
operator-supplied static files and terminates TLS, publishes Authentication API as `/auth/*`
(`/auth/<operation>` → `/api/auth/<operation>`, `/auth/admin/*` → `/api/admin/*`, internal routes
unchanged), proxies `/api-a/api/*` and `/api-b/api/*`, rewrites the refresh cookie to `Path=/auth`
and administrative `Location` headers to the public form, applies the Phase 7
first limiting layer, and is the only published port; backends publish nothing and auth-api trusts
only the frontend's fixed internal address. Direct backend ports used by acceptance move to a
test-only override. (4) Documented SQLite backup (`sqlite3 .backup`, consistent under writes) and
restore, proven in a disposable environment. (5) `tests/acceptance/phase-8.sh` runs the end-to-end
scenario from empty storage, lifecycle and `down -v` survival, backup/restore, and chains the
Phase 7→1 regression. No migration, domain change, endpoint, or extra permanent service.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`, SDK pinned by `global.json`), stable C# 14

**Primary Dependencies**: ASP.NET Core 10.0.12 Minimal APIs; `Microsoft.Extensions.Logging`
(console provider + first-party file provider); `System.Threading.Channels` (shared framework).
**New packages** (both authorized by Technical Constraints §33, Api project only):
`Microsoft.AspNetCore.OpenApi` 10.0.12 (matches the 10.0.12 stack; its `Microsoft.OpenApi`
transitive dependency is pinned centrally) and `Scalar.AspNetCore` 2.17.9 (published 2026-09-24,
≥ 2 weeks old, no package dependencies). Frontend: official `nginx` stable Alpine image pinned by
tag and digest (the digest already validated in Phase 7). Acceptance only: host `sqlite3` CLI,
`openssl`, `curl`, `jq`, Docker Compose; the Phase 6 Mailpit override for email.

**Storage**: Existing SQLite (no schema change). New host bind mount for logs (`/app/logs`).
Frontend static files and TLS certificate/key are operator-supplied read-only bind mounts.

**Testing**: xUnit.net v3 on Microsoft Testing Platform. Unit tests for the file provider (line
format, UTC daily rotation, retention, concurrency, disposal flush) with a controlled
`TimeProvider` and temporary directories; integration tests (`WebApplicationFactory`) for the new
events, the file sink in the real host, OpenAPI coverage in Development and absence in
Production, and the log-directory fail-fast. Compose acceptance `tests/acceptance/phase-8.sh` on
disposable storage, chaining Phase 7→1 regression.

**Target Platform**: Linux containers (official .NET 10 runtime images, non-root; official Nginx).

**Project Type**: Internal REST web service plus reference consumer, deployed behind an Nginx
frontend/reverse proxy.

**Performance Goals**: None invented. Logging must not perform significant blocking I/O inside
`ILogger.Log` (FR-003).

**Constraints**: Exactly four services (FR-009); no backend port published in production
(FR-012); critical storage on host bind mounts surviving `down -v` (FR-016); no new endpoint,
entity, table, or product feature (FR-023); no logging framework, collector, or observability
platform (NFR-003); acceptance never touches real production paths (FR-018).

**Scale/Scope**: One logging provider (+ options, registration), six events, OpenAPI/Scalar
registration and metadata on 20 endpoints, one Nginx configuration, Compose rewrite plus two test
overrides, backup/restore procedure, one acceptance script, harness adaptation of Phase 1–7
scripts, operations documentation and Gate G8 evidence.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline authority and traceability | PASS | PASS | Public `/auth/*` URL contract fixed by the 2026-10-08 clarification (spec FR-010a/FR-011) within SRS §4.2's configurable prefixes; internal routes and Phase 1–7 contracts unchanged. Traces to SRS NFR-LOG-001–005, NFR-DOC-001–004, NFR-DEPLOY-001–014, NFR-DB-INIT, NFR-HEALTH-001–005, NFR-BACKUP-001–006, §4.2/4.3; Technical Constraints §12–15, §28–31, §33–39; Roadmap §14/G8. Implementation choices (cookie-path rewrite, fixed proxy address, log line format, backup tool) are recorded in [research.md](research.md) as choices, not requirements. |
| II. Incremental vertical capabilities | PASS | PASS | Only Roadmap §14 scope: operations, documentation, deployment, persistence, backup, acceptance. No functional feature; missing events are logging obligations of NFR-LOG-002 assigned to §14.2. Earlier-phase defects, if found, are routed to their phase (FR-023). |
| III. Hexagonal boundaries and feature slices | PASS | PASS | File provider and options in Infrastructure (`Logging/`), registered by the composition root; new events beside the Identity adapters that know them; OpenAPI/Scalar and endpoint metadata in Api only; Domain/Application untouched. `Program.cs` gains registration and environment-gated mapping only. |
| IV. Deliberate simplicity and dependency control | PASS | PASS | Two baseline-authorized packages justified by FR-006/007; first-party provider mandated by TC §15.3 instead of Serilog/NLog; no image is built for the frontend (official Nginx + mounted config); no gateway, collector, scheduler, or backup container. |
| V. Security by construction | PASS | PASS | TLS terminated at Nginx; backends unexposed; private key mounted only into auth-api (read-only), public key only into consumers; forwarded headers trusted only from the frontend's fixed address; docs absent in production; log sink inherits the secret-free events and records exceptions without messages. |
| VI. Tests of implemented behavior | PASS | PASS | Provider unit tests with controlled time (no sleeps); host integration tests for events, file sink, OpenAPI exposure; Compose acceptance for startup, routing, isolation, lifecycle, `down -v`, backup/restore, E2E, regression — see [quickstart.md](quickstart.md). Lockout recovery in acceptance is driven by moving the persisted lockout end, not by waiting. |
| VII. Persistence ownership and deployment integrity | PASS | PASS | SQLite, key ring, private key, and logs on host bind mounts with documented ownership/permissions; migrations and bootstrap remain in-process at startup; exactly `frontend`, `auth-api`, `api-a`, `api-b`; backup via SQLite's backup API without a permanent service; restore demonstrated in a disposable environment. |

No constitution violation requires a complexity exception.

## Design

### Logging

- `Infrastructure/Logging/PersistentFileLoggerProvider` (`[ProviderAlias("File")]`, so
  `Logging:File:LogLevel` filters apply), `PersistentFileLogger`, `PersistentFileLoggerOptions`
  (`Logging:File:Directory` required, `Logging:File:RetentionDays` default 30, ≥ 1).
- `Log` formats one line on the calling thread (UTC from `TimeProvider`, level, category,
  `EventId` when non-zero, `Activity.Current` trace/span, message, exception type and stack trace
  without message) and enqueues it on an unbounded `Channel<string>`; one background task appends
  to `auth-YYYY-MM-DD.log` (UTF-8, no BOM), switching file when the UTC date changes and deleting
  `auth-*.log` older than the retention at startup and at each rotation; `Dispose` completes the
  channel and drains it.
- Registered by `AddAuthenticationInfrastructure` after validating the directory with the existing
  usable-directory probe (missing/unwritable → startup fails naming `Logging:File:Directory`).
- Console: `AddSimpleConsole` single-line with UTC timestamps.
- New `SecurityEvents`: `LoginSucceeded`, `UserCreated`, `UserEnabled`, `UserDisabled`,
  `UserRoleAssigned`, `UserRoleRemoved` (user id, role name, UTC, trace/span; never email or
  password). Role events come from the diff computed in `ReplaceRolesAsync`.

### OpenAPI and Scalar

- `builder.Services.AddOpenApi()` with a document transformer adding the `Bearer` HTTP security
  scheme and an operation transformer adding the requirement to endpoints carrying authorization
  metadata. Endpoints gain `WithSummary`, `Produces<T>`, `ProducesProblem`, and
  `ProducesValidationProblem`-equivalent problem responses matching their existing results;
  behavior unchanged.
- `if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.MapScalarApiReference(o => read-only) }`:
  `HideTestRequestButton`, `HideClientButton`, no persistent authentication, telemetry off.
  Production maps neither, and Nginx never routes `/openapi` or `/scalar`.

### Deployment

```text
browser ──HTTPS──> frontend (nginx, :443/:80→443, static files + reverse proxy)
                     /                → operator-supplied static files (SPA fallback)
                     /auth/admin/*    → auth-api:8080/api/admin/*  (Location /api/admin/ → /auth/admin/)
                     /auth/*          → auth-api:8080/api/auth/*   (cookie Path /api/auth → /auth)
                     /api-a/api/*     → api-a:8080/api/*
                     /api-b/api/*     → api-b:8080/api/*
                     /auth/api/auth/login, /auth/health/*, /auth/openapi, /auth/scalar → 404 (map to no route)
                     anything else under /api-a, /api-b → 404
internal network (fixed subnet) ── auth-api trusts only the frontend's fixed address
```

Details in [contracts/deployment-topology.md](contracts/deployment-topology.md).

### Files

```text
src/Authentication.Infrastructure/Logging/PersistentFileLoggerProvider.cs   # new
src/Authentication.Infrastructure/Logging/PersistentFileLogger.cs           # new
src/Authentication.Infrastructure/Logging/PersistentFileLoggerOptions.cs    # new
src/Authentication.Infrastructure/DependencyInjection.cs                    # file logging registration + validation
src/Authentication.Infrastructure/Identity/SecurityEvents.cs                # six new events
src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs   # LoginSucceeded
src/Authentication.Infrastructure/Identity/UserAdministration.cs            # created, enabled/disabled, role events
src/Authentication.Api/Authentication.Api.csproj                            # + OpenApi, Scalar
src/Authentication.Api/Documentation/ApiDocumentationRegistration.cs        # new: AddOpenApi, transformers, dev mapping
src/Authentication.Api/Documentation/BearerSecuritySchemeTransformer.cs     # new
src/Authentication.Api/Features/**/*Endpoint(s).cs                          # summaries + response metadata only
src/Authentication.Api/Program.cs                                           # console format, docs registration/mapping
src/Authentication.Api/appsettings.json                                     # Logging:File placeholders
Directory.Packages.props                                                    # + Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore, Microsoft.OpenApi pin
deploy/frontend/nginx.conf                                                  # new: production frontend/reverse proxy
compose.yml                                                                 # four services, internal network, no backend ports, logs mount
.env.example                                                                # Phase 8 section
tests/acceptance/compose.direct-access.yml                                  # new, acceptance only: publishes backend ports
tests/acceptance/frontend-test-page/index.html                              # new, acceptance only, not a product
tests/acceptance/deployment-env.sh                                          # new: shared disposable frontend/TLS/log setup
tests/acceptance/phase-{1..7}.sh                                            # source deployment-env.sh; assertions unchanged
tests/acceptance/compose.reference-proxy.yml                                # reuse compose.yml network settings
tests/acceptance/phase-8.sh                                                 # new: E2E, lifecycle, backup/restore, regression
tests/Authentication.UnitTests/Infrastructure/PersistentFileLoggerProviderTests.cs  # new
tests/Authentication.UnitTests/Infrastructure/MutableTimeProvider.cs               # new test fake
tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs    # temp log directory, environment option
tests/Authentication.IntegrationTests/Scenarios/OperationalEventsTests.cs          # new
tests/Authentication.IntegrationTests/Scenarios/ApiDocumentationTests.cs           # new
docs/phase-8-operations.md                                                  # new: deployment, paths, backup/restore, Gate G8 evidence
```

## Project Structure

### Documentation (this feature)

```text
specs/008-phase-8-operations-final-integration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── deployment-topology.md
│   ├── operational-log.md
│   └── openapi-coverage.md
├── checklists/
│   └── requirements.md
└── tasks.md                                   # generated by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── Authentication.Domain/                     # unchanged
├── Authentication.Application/                # unchanged
├── Authentication.Infrastructure/
│   ├── Logging/                               # file provider
│   └── Identity/                              # events
├── Authentication.Api/
│   ├── Documentation/                         # OpenAPI + Scalar (Development only)
│   └── Features/                              # response metadata only
└── ReferenceConsumer.Api/                     # unchanged

deploy/frontend/nginx.conf
compose.yml
docs/phase-8-operations.md

tests/
├── Authentication.UnitTests/Infrastructure/
├── Authentication.IntegrationTests/{Infrastructure,Scenarios}/
└── acceptance/
    ├── compose.direct-access.yml
    ├── deployment-env.sh
    ├── frontend-test-page/
    └── phase-8.sh
```

**Structure Decision**: Keep the four-project solution. Logging is infrastructure behind
`ILogger<T>`; documentation is an HTTP boundary concern of the composition root. The frontend is a
configuration of the official Nginx image (`deploy/frontend/`), not a new project; the static files
are operator input. All direct-access and test-page assets live under `tests/acceptance`.

## Complexity Tracking

Not applicable: the pre-research and post-design constitution checks pass without exceptions.
