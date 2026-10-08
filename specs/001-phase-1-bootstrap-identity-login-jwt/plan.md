# Implementation Plan: Phase 1 — Bootstrap, Identity, Admin, Login and JWT

**Branch**: `001-phase-1-bootstrap-identity-login-jwt` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-phase-1-bootstrap-identity-login-jwt/spec.md`

## Summary

Deliver the first executable Authentication API vertical slice: a .NET 10 Minimal API starts
from empty externally persisted SQLite storage, applies its own EF Core migrations, atomically
bootstraps the fixed administrator identity, authenticates by normalized email through ASP.NET
Core Identity, issues a 15-minute-by-default RS256 access token, and exposes liveness/readiness.
The design uses the prescribed four-project hexagonal structure and feature slices, with only
two current-use Application ports for credential authentication and token issuance. Docker
bind mounts keep SQLite and RSA material outside the Compose project lifecycle.

## Technical Context

**Language/Version**: .NET 10, target framework `net10.0`, stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs and built-in DI/health/logging;
ASP.NET Core Identity 10.x with EF stores; EF Core 10.x with the official SQLite provider;
Microsoft IdentityModel libraries for RS256 issuance; development-only EF design tooling

**Storage**: One Authentication API-owned SQLite file plus an externally supplied RSA PEM key
pair; the database and private key live on host bind mounts (or an explicitly external volume)
outside the Compose project lifecycle

**Testing**: xUnit.net v3 on Microsoft Testing Platform; xUnit assertions;
`Microsoft.AspNetCore.Mvc.Testing` 10.x with `WebApplicationFactory`; real SQLite in-memory only
for short SQL scenarios and temporary SQLite files for migration, restart, and persistence tests;
controlled `TimeProvider` for JWT time assertions

**Target Platform**: Linux container built from official .NET 10 SDK/runtime images; non-root
runtime; Docker Compose for development and Phase 1 acceptance

**Project Type**: Internal REST web service

**Performance Goals**: No invented latency or throughput target. Password security takes
precedence over authentication latency; the service targets the SRS small-to-medium SQLite
installation profile.

**Constraints**: Hexagonal Architecture plus feature vertical slices; automatic idempotent
migrations/bootstrap before normal traffic; generic anti-enumeration login failures; RS256 with
one persistent private key; UTC and `System.TimeProvider`; ProblemDetails errors; build/test/
startup/feature/regression must pass; no future-phase components or unauthorized dependencies

**Scale/Scope**: Phase 1 exposes three endpoints (`POST /api/auth/login`, `GET /health/live`,
`GET /health/ready`), persists Identity users/roles only, and runs one Authentication API
container. Business API integration and the final four-service topology are later phases.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| Baseline precedence and traceability | PASS | PASS | `spec.md` and all design artifacts cite the SRS, Technical Constraints, Roadmap Phase 1, and G1; the SRS-controlled 15-minute default is retained. |
| Eight-phase boundary | PASS | PASS | Only Phase 1 bootstrap, login, access-token issuance, health, and current persistence are designed. Refresh, APIs A/B, administration, password flows, hardening, and final operations remain absent. |
| Hexagonal dependencies and vertical slices | PASS | PASS | Domain is technology-free; Application owns current-use ports; Infrastructure owns Identity/EF/SQLite/JWT adapters; Api composes and mounts endpoints. |
| Simplicity and dependency control | PASS | PASS | No generic repository, MediatR, AutoMapper, FluentValidation, external DI, mocking framework, future port, extra DbContext, or additional service is introduced. |
| Security by construction | PASS | PASS | Identity owns password verification/hashing and failure accounting; Microsoft IdentityModel signs RS256; key and secrets stay external and out of logs. |
| Current-behavior verification | PASS | PASS | Consolidated integration scenarios exercise real Identity/EF/SQLite/JWT behavior; deployment evidence covers Phase 1 persistence without future capabilities. |
| Persistence and deployment integrity | PASS | PASS | Application-owned migrations/bootstrap, host-mounted SQLite/RSA, no migration/bootstrap service, and a non-root runtime are explicit. |
| Repository discipline | PASS | PASS | Required projects, central package versions, pinned .NET 10 SDK, nullable/code-style settings, file-scoped namespaces, and one top-level type per file are planned. |

No constitutional violations require a complexity exception.

## Phase 0: Research Decisions

Research findings are consolidated in [research.md](research.md). All plan decisions are resolved;
there are no `NEEDS CLARIFICATION` items.

Key outcomes:

- Use native Identity storage and behavior without an artificial Domain user or role model.
- Keep only credential-validation and access-token issuance ports in Application.
- Apply migrations, then run an atomic fixed-ID bootstrap before serving normal traffic.
- Fail startup on invalid configuration, invalid RSA material, or migration/bootstrap failure;
  readiness returns `503` only when a running initialized service loses database availability.
- Return a minimal access-token response and identical ProblemDetails for all invalid credential
  states that exist in the current phase.
- Defer all dependencies and infrastructure assigned to later roadmap phases.

## Phase 1: Design and Contracts

### Design Outputs

- [data-model.md](data-model.md) defines the Identity-owned persisted state, transient token,
  external RSA material, bootstrap invariants, and lifecycle transitions.
- [contracts/authentication-api.openapi.yaml](contracts/authentication-api.openapi.yaml) defines
  only login, liveness, and readiness contracts using OpenAPI 3.1 syntax as a design artifact.
- [quickstart.md](quickstart.md) defines focused automated and deployment validation for G1.

### Runtime Design

1. Api validates Phase 1 configuration at startup: SQLite location/connection, JWT issuer,
   audience, lifetime, and readable RSA private PEM path.
2. Infrastructure applies repository-shipped migrations to the one SQLite database.
3. Infrastructure runs bootstrap inside a database transaction. Fixed stable IDs distinguish
   the built-in administrator and role from mutable email/name values. If the built-in user
   already exists, bootstrap performs no mutations; otherwise it ensures the fixed role, creates
   the user with Identity, and assigns the role atomically.
4. The host begins normal request processing only after successful initialization.
5. The Login slice normalizes the supplied email through Identity, performs Identity password
   verification with failed-attempt accounting, and performs Identity-hasher-equivalent work for
   an unknown email before returning the same generic `401` ProblemDetails.
6. On success, the handler obtains current role names and calls the token-issuer port. The adapter
   uses `TimeProvider`, a unique `jti`, the stable Identity user ID as `sub`, and the configured
   key/issuer/audience/lifetime to sign RS256.
7. Liveness reports only process health. Readiness checks that startup completed and SQLite is
   currently available. Public health payloads contain no component or configuration detail.

The baseline initial password `admin` is created through Identity, never through custom hashing.
The Phase 1 Identity password-policy defaults therefore permit that explicit five-character
credential and remain externally configurable. Full security-hardening policy and its acceptance
tests remain assigned to Phase 7. Phase 1 operational guidance explicitly labels this credential
as temporary and requiring replacement after first access; no persisted replacement flag or
password-change behavior is introduced before Phase 5.

### Verification Design

The focused suite uses four consolidated integration scenarios:

1. File-backed SQLite startup, migrations, first bootstrap, second startup, exactly-one role/user,
   initial credential validity, and preservation of an intentionally modified admin value.
2. Valid login plus equivalent unknown-email/wrong-password `401` contracts and persisted
   failed-attempt accounting.
3. Cryptographic JWT verification with test public material, required claims, stable subject,
   issuer/audience, RS256, UTC timestamps, default/configured lifetime, and response expiry.
4. Healthy live/ready responses plus deterministic configuration/database initialization failure
   proving the service never announces readiness and diagnostics reveal no secret material.

Unit tests are added only if a pure deterministic rule cannot be covered meaningfully by these
integration scenarios. A disposable Compose demonstration proves empty startup, restart, and
Phase 1 SQLite/RSA survival through `docker compose down -v`. Phase 8 repeats teardown acceptance
for the complete topology and later persistent assets.

## Project Structure

### Documentation (this feature)

```text
specs/001-phase-1-bootstrap-identity-login-jwt/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── authentication-api.openapi.yaml
└── checklists/
    └── requirements.md
```

`tasks.md` is deliberately absent; it is generated by `$speckit-tasks` after plan review.

### Source Code (repository root)

```text
Authentication.slnx
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
compose.yml
.env.example
docs/
└── phase-1-operations.md
src/
├── Authentication.Domain/
│   └── Authentication.Domain.csproj
├── Authentication.Application/
│   ├── Authentication.Application.csproj
│   └── Features/
│       └── Login/
│           ├── AccessToken.cs
│           ├── AuthenticatedIdentity.cs
│           ├── IAccessTokenIssuer.cs
│           ├── IIdentityCredentialValidator.cs
│           ├── LoginCommand.cs
│           ├── LoginHandler.cs
│           └── LoginOutcome.cs
├── Authentication.Infrastructure/
│   ├── Authentication.Infrastructure.csproj
│   ├── DependencyInjection.cs
│   ├── Identity/
│   │   └── IdentityCredentialValidator.cs
│   ├── Persistence/
│   │   ├── AuthenticationDbContext.cs
│   │   ├── DatabaseInitializer.cs
│   │   └── Migrations/
│   ├── Health/
│   │   └── SqliteHealthCheck.cs
│   └── Security/
│       ├── JwtAccessTokenIssuer.cs
│       └── JwtOptions.cs
└── Authentication.Api/
    ├── Authentication.Api.csproj
    ├── Dockerfile
    ├── Program.cs
    ├── appsettings.json
    └── Features/
        ├── Health/
        │   ├── HealthEndpoints.cs
        │   └── HealthStatusResponse.cs
        └── Login/
            ├── LoginEndpoint.cs
            ├── LoginRequest.cs
            └── LoginResponse.cs
tests/
├── Authentication.UnitTests/
│   └── Authentication.UnitTests.csproj
├── Authentication.IntegrationTests/
│   ├── Authentication.IntegrationTests.csproj
│   ├── Infrastructure/
│   │   └── AuthenticationApiFactory.cs
│   └── Scenarios/
│       ├── BootstrapLifecycleTests.cs
│       ├── HealthLifecycleTests.cs
│       └── LoginAndJwtTests.cs
└── acceptance/
    └── phase-1.sh
```

**Structure Decision**: Use exactly the four product projects and two test projects mandated by
Technical Constraints §5. `Authentication.Domain` intentionally contains no artificial Identity
model in Phase 1. The current Login slice owns its Application types and Api endpoint types;
Infrastructure groups only the adapters required for Identity, persistence, health, and signing.
The shell acceptance script is deployment evidence, not an additional test project.

## Complexity Tracking

No violations or complexity exceptions are present.
