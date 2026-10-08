# Implementation Plan: Phase 2 — JWT Validation in Consumer APIs

**Branch**: `002-jwt-validation-consumer-apis` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-jwt-validation-consumer-apis/spec.md`

## Summary

Prove centralized authentication across services: one minimal reference consumer, an ASP.NET
Core 10 Minimal API, is deployed twice in Docker Compose as `api-a` and `api-b`. Each instance
validates Authentication API RS256 access tokens locally with JwtBearer. Validation uses only a
read-only mounted public key, the shared issuer and audience, `RS256` as the only algorithm, and
an explicit 30-second clock tolerance. Each instance exposes a caller endpoint (authenticated)
and an administrator endpoint (`Administrator` role). Invalid tokens get `401` and missing roles
get `403`. Authentication API code and contracts are unchanged.

## Technical Context

**Language/Version**: .NET 10, target framework `net10.0`, stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs and built-in DI/authorization/logging;
`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 (new, authorized by Technical
Constraints §33, consumer only); Microsoft IdentityModel (transitive) for validation; existing
Phase 1 packages for Authentication API and tests

**Storage**: None for the consumer. Public PEM supplied as a single read-only bind-mounted file.
Phase 1 SQLite and private key unchanged and never visible to consumers

**Testing**: xUnit.net v3 on Microsoft Testing Platform; `WebApplicationFactory` for both the
consumer (two configurations) and Authentication API; real JwtBearer validation of real RS256
tokens; disposable RSA pairs; no sleeps, mocks, or new test project; Compose acceptance script

**Target Platform**: Linux containers from official .NET 10 SDK/runtime images; non-root;
Docker Compose for development and Phase 2 acceptance

**Project Type**: Internal REST web services (Authentication API plus a reference consumer)

**Performance Goals**: No invented targets. Validation is local and in-process (NFR-PERF-002)

**Constraints**: No call to Authentication API per request; no JWKS/metadata; private key never
in consumers; explicit, consistent, bounded clock tolerance; fail-fast configuration; no
error detail in `401`/`403`; Phase 1 behavior unchanged; no later-phase components

**Scale/Scope**: Consumer exposes `GET /api/caller`, `GET /api/caller/administrator`,
`GET /health/live`; Compose grows from one to three services (`auth-api`, `api-a`, `api-b`);
no proxy or frontend

**Roadmap boundaries**: Reverse-proxy routing, frontend, final four-service topology, and the
production rule that backends are not published directly (NFR-DEPLOY-004) remain Phase 8.
Consumer ports are published to the host only for development and acceptance. Revocation,
refresh, administration, and JWKS remain in their own phases or excluded.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| Baseline precedence and traceability | PASS* | PASS* | Spec and artifacts cite SRS FR-JWT-003/004/010–012, FR-KEY-005, FR-AUTHZ-003–005, NFR-TIME-003/004, NFR-DEPLOY-008/009, Roadmap §8/G2. *The consumer project is outside Technical Constraints §5.2. The project owner explicitly decided it on 2026-10-07, and it must be recorded as DEC-009 (see Complexity Tracking). |
| Eight-phase boundary | PASS | PASS | Only local validation, minimal authorization, and their deployment are designed. Refresh, administration, revocation, JWKS, proxy, and frontend are absent. |
| Hexagonal dependencies and vertical slices | PASS | PASS | Authentication API structure is untouched. The consumer has one feature slice and no business core, so there are no layers to separate. It references no Authentication project. |
| Simplicity and dependency control | PASS | PASS | One new package, authorized for this purpose and documented here. One project deployed twice instead of two. No new test project, mock library, or shared library. |
| Security by construction | PASS | PASS | IdentityModel validates through JwtBearer with a single algorithm and required signature/expiry. Only the public key is mounted, and private-key PEMs are rejected at startup. No error details or secrets appear in responses or logs. |
| Current-behavior verification | PASS | PASS | Real tokens are validated by the real JwtBearer handler in both configurations. The end-to-end issuance → validation path is tested. Compose demonstrates Auth-API-down acceptance. Phase 1 suite and `phase-1.sh` are rerun as regression. |
| Persistence and deployment integrity | PASS | PASS | No new persistence. Consumers cannot reach SQLite. No migration/bootstrap service. Non-root images. Phase 1 RD-008 assets are unchanged. |
| Repository discipline | PASS | PASS | Central package version, pinned SDK, nullable/analyzers, file-scoped namespaces, and one type per file apply to the new project. The project is added to `Authentication.slnx`. |

## Phase 0: Research Decisions

All decisions are consolidated in [research.md](research.md); no `NEEDS CLARIFICATION` remains.

Key outcomes:

- One reference consumer project deployed as `api-a` and `api-b` (project-owner decision).
- JwtBearer with an explicit `TokenValidationParameters`: RS256 only, signature/issuer/audience/
  lifetime required, no authority or metadata, original claim names (`sub`, `role`), and no
  error details.
- `Jwt:ClockSkewSeconds` is required, bounded to 0–60, uses the reference value 30, and is
  supplied to both consumers from one Compose variable.
- Single-file read-only public-key mount. The consumer rejects private-key PEMs.
- No Authentication API code or contract changes.
- Tests extend the existing integration project. The consumer exposes a public marker type for
  `WebApplicationFactory`.

## Phase 1: Design and Contracts

### Design Outputs

- [data-model.md](data-model.md) defines validation configuration, the presented token's claim
  use, the validated caller identity, request outcomes, and external key material.
- [contracts/reference-consumer-api.openapi.yaml](contracts/reference-consumer-api.openapi.yaml)
  defines the consumer endpoints, the bearer scheme, and the `401`/`403` behavior.
- [quickstart.md](quickstart.md) defines automated and Compose validation for G2.

### Runtime Design

1. The consumer binds `Service:Name`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:PublicKeyPath`, and
   `Jwt:ClockSkewSeconds`, validates them, and reads the PEM. Startup fails, naming only the
   setting, if any value is invalid or if the PEM is not a public key.
2. JwtBearer is registered as the default scheme with the explicit validation parameters. An
   authorization policy `Administrator` requires role `Administrator`.
3. `GET /api/caller` requires authorization, and `GET /api/caller/administrator` requires the
   `Administrator` policy. Both return `{ service, subject, roles }` built from the validated
   principal. `GET /health/live` is anonymous.
4. Authentication failures produce `401` with `WWW-Authenticate: Bearer` and no error
   description. Authorization failures produce `403`. Neither has a body.
5. Compose adds `api-a` and `api-b`, built from `src/ReferenceConsumer.Api/Dockerfile`. They
   share `AUTH_JWT_ISSUER`, `AUTH_JWT_AUDIENCE`, and `AUTH_JWT_CLOCK_SKEW_SECONDS`, each mounts
   `AUTH_JWT_PUBLIC_KEY_HOST_FILE` read-only, and each has its own `Service__Name` and host port.
   Neither depends on `auth-api` at startup.

### Verification Design

The focused suite adds one consolidated integration scenario class:

1. Issuance → local validation: a real Authentication API factory and two consumer instances
   share one RSA pair. A logged-in administrator token is accepted by both, with the same `sub`
   and roles. After the Authentication API host is disposed, both still accept the token.
2. Rejection matrix (both instances): no token, malformed, forged signature, unsigned or
   non-RS256, expired beyond tolerance, wrong issuer, and wrong audience → `401`. The response
   has no body and no error description.
3. Authorization: a validly signed token with role `Operator` (not `Administrator`) → `200` on
   `/api/caller` and `403` on `/api/caller/administrator`. A token with no role behaves the
   same. No token → `401` on both.
4. Startup failure: missing issuer/audience, out-of-range tolerance, missing public key file,
   and a private-key PEM as the public key all terminate startup, naming the setting without
   leaking key material.

`tests/acceptance/phase-2.sh` starts the three-service Compose stack on disposable storage. It
obtains a real token, checks `200`/`401` on both consumers, stops `auth-api` and re-checks, and
inspects consumer mounts for the absence of the private key. It then runs `phase-1.sh` as
regression.

## Project Structure

### Documentation (this feature)

```text
specs/002-jwt-validation-consumer-apis/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── reference-consumer-api.openapi.yaml
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
Authentication.slnx                       # + ReferenceConsumer.Api
Directory.Packages.props                  # + Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12
compose.yml                               # + api-a, api-b services
.env.example                              # + public key file, clock skew, consumer ports
docs/
└── phase-2-operations.md
src/
├── Authentication.*                      # unchanged (Phase 1)
└── ReferenceConsumer.Api/
    ├── ReferenceConsumer.Api.csproj
    ├── Dockerfile
    ├── Program.cs
    ├── ReferenceConsumerEntryPoint.cs
    ├── appsettings.json
    ├── Security/
    │   ├── ConsumerJwtOptions.cs
    │   └── JwtValidationRegistration.cs
    └── Features/
        ├── Caller/
        │   ├── CallerEndpoints.cs
        │   └── CallerIdentityResponse.cs
        └── Health/
            └── LivenessEndpoint.cs
tests/
├── Authentication.IntegrationTests/
│   ├── Infrastructure/
│   │   ├── ReferenceConsumerFactory.cs
│   │   └── TestTokenMinter.cs
│   └── Scenarios/
│       └── ConsumerValidationTests.cs
└── acceptance/
    └── phase-2.sh
```

**Structure Decision**: Authentication API keeps its four baseline projects unchanged. One
additional product project, `ReferenceConsumer.Api`, hosts both Business API A and B
demonstrations. The project owner decided this explicitly, as the documented exception below.
Consumer verification lives in the existing integration test project.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Fifth product project (`ReferenceConsumer.Api`) outside Technical Constraints §5.2 | Roadmap §8/G2 requires Business API A and B to validate tokens locally and run as Compose services. The real business APIs are separate repositories with no code yet. The project owner explicitly chose this option on 2026-10-07; record it as DEC-009 in the roadmap decision log during implementation. | Two projects duplicate identical code. Waiting for CanchaBackend/buffetBackend leaves G2 unclosable here. Test-only hosting was rejected by clarification Q2. |
| Consumer without the hexagonal four-project split | It has no business logic or infrastructure boundary to protect. A layered split would be empty ceremony. | The four-layer structure adds projects with no current use (Constitution IV). |
