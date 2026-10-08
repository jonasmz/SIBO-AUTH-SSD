# Implementation Plan: Phase 4 — Refresh Tokens, Renewable Sessions and Logout

**Branch**: `004-phase-4-refresh-sessions-logout` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-phase-4-refresh-sessions-logout/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 4, Gate G4 (Roadmap §10)

## Summary

Extend successful login with a browser-only renewable session, rotate opaque refresh credentials
through `POST /api/auth/refresh`, revoke the current family through idempotent logout, and let an
Administrator revoke every family for a user. Authentication API stores a SHA-256 verifier for each
256-bit random credential in its existing SQLite database; the raw value exists only long enough to
set an HttpOnly, SameSite=Strict cookie. A persisted family is the revocation and absolute-expiry
boundary, while a credential chain records consumption and replacement.

Rotation and all competing revocation operations execute in short serializable SQLite transactions.
The transaction consumes exactly one current credential, creates at most one replacement in the same
family, and treats later presentation of the consumed credential as replay that irreversibly revokes
the family. Access JWT validation remains local and stateless in API A/B.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`), stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs, built-in authentication/authorization,
ProblemDetails and cookie primitives; ASP.NET Core Identity 10.0.12; EF Core SQLite 10.0.12;
Microsoft IdentityModel for existing RS256 JWT issuance. Refresh generation and hashing use
`System.Security.Cryptography`; no new NuGet dependency.

**Storage**: Existing single SQLite file and `AuthenticationDbContext`, exclusively owned by
Authentication API. One migration adds renewable-session-family and refresh-credential tables,
indexes, foreign keys and constraints. Raw credentials are never persisted.

**Testing**: xUnit.net v3 on Microsoft Testing Platform; `WebApplicationFactory` with real Identity,
Minimal APIs and SQLite. Temporary SQLite files cover restart/migration behavior; real SQLite
transactions cover concurrency; `ControlledTimeProvider` covers expiry without sleeps. A Phase 4
Compose acceptance script demonstrates the full lifecycle and reruns Phase 1–3 regression scripts.

**Target Platform**: Linux containers from official .NET 10 images, running non-root. Existing
Compose topology (`auth-api`, `api-a`, `api-b`) and host-mounted SQLite lifecycle remain unchanged.

**Project Type**: Internal REST web service (Authentication API) plus unchanged reference consumer.

**Performance Goals**: No invented throughput target. Refresh-token lookup is a unique indexed hash
lookup; family/user revocation uses indexed foreign keys. Transactions remain limited to one family
or one user's active families.

**Constraints**: Seven-day configurable absolute lifetime; no sliding expiry. Exact configured
frontend-origin validation on refresh/logout and no same-origin CORS policy. At most one concurrent
consumer per credential. Generic `401` for every unusable refresh state. Logout remains idempotent.
No token or hash in logs. No JWT blacklist, introspection, Redis, second database or new service.
General refresh rate limiting remains deferred to Phase 7 per the approved clarification.

**Scale/Scope**: Three new endpoints/endpoint extensions (`refresh`, `logout`, admin
`revoke-sessions`), login cookie issuance, disablement revocation, two new persisted entities and one
migration. No frontend, session listing, password workflow, proxy deployment or future-phase types.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline authority and traceability | PASS | PASS | [research.md](research.md) traces token, cookie, rotation, revocation, persistence and verification decisions to SRS FR-REFRESH-*, FR-REFRESH-ENDPOINT-*, FR-LOGOUT-*, FR-USER-008–014, NFR-CSRF-*, NFR-TIME-*, CR-DATA-*; Technical Constraints §§8–11, 17–23; and Roadmap §10/G4. The Phase 7 rate-limit conflict follows the spec clarification. |
| II. Incremental vertical capabilities | PASS | PASS | The design implements only Roadmap §10: renewable login, refresh rotation/replay, logout, administrative and disablement revocation. Password workflows, rate limiting, frontend, proxy and session dashboards are absent. |
| III. Hexagonal boundaries and feature slices | PASS | PASS | Domain owns family/credential state transitions. Application owns session commands/outcomes and ports. Infrastructure owns EF/Identity/cryptography adapters. API owns cookies, Origin checks, ProblemDetails and endpoint mapping. Consumers receive no session dependency. |
| IV. Deliberate simplicity and dependency control | PASS | PASS | Existing framework and package set is sufficient. Two concrete entities model the distinct family and credential lifecycles; there is no generic repository, unit-of-work facade, custom clock, token framework or distributed component. |
| V. Security by construction | PASS | PASS | 256-bit CSPRNG credentials, SHA-256-only persistence, restrictive HttpOnly cookie, exact Origin validation, generic refresh failure, serialized rotation, family-wide replay containment and secret-free structured logs are explicit contract/design rules. Identity and RS256 responsibilities remain unchanged. |
| VI. Tests of implemented behavior | PASS | PASS | [quickstart.md](quickstart.md) defines focused unit/integration/acceptance evidence for all NFR-002 behaviors, deterministic expiry, real concurrent SQLite consumption, restart persistence, cookie attributes, log scanning, consumer-local validation and Phase 1–3 regression. |
| VII. Persistence ownership and deployment integrity | PASS | PASS | Both tables use the existing Auth-owned SQLite database, startup migration path and host mount. No consumer schema access, migration container, external bootstrap, new volume type or additional database is introduced. |

No constitution violation requires a complexity exception.

## Project Structure

### Documentation (this feature)

```text
specs/004-phase-4-refresh-sessions-logout/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── authentication-api-sessions.openapi.yaml
└── tasks.md                                      # generated by /speckit-tasks, not this command

docs/
└── phase-4-operations.md                         # Gate G4 commands and observed PASS evidence
```

### Source Code (repository root)

```text
src/
├── Authentication.Domain/
│   └── Sessions/
├── Authentication.Application/
│   └── Features/
│       ├── Login/                               # extend successful-login orchestration
│       └── Sessions/                            # commands, outcomes and ports
├── Authentication.Infrastructure/
│   ├── Identity/                                # user checks and disablement extension
│   ├── Persistence/
│   │   └── Migrations/                          # Phase 4 schema migration
│   └── Sessions/                                # EF-backed issuance/rotation/revocation
├── Authentication.Api/
│   └── Features/
│       ├── Login/                               # existing response + refresh cookie
│       ├── Sessions/                            # refresh/logout, cookie and Origin boundary
│       └── Users/                               # revoke-sessions endpoint
└── ReferenceConsumer.Api/                       # unchanged local JWT validation

tests/
├── Authentication.UnitTests/
│   └── Domain/
├── Authentication.IntegrationTests/
│   └── Scenarios/
└── acceptance/
    ├── phase-1.sh
    ├── phase-2.sh
    ├── phase-3.sh
    └── phase-4.sh
```

**Structure Decision**: Preserve the existing four-project hexagonal solution and vertical feature
slices. Session state belongs to Domain/Application/Infrastructure within Authentication API;
browser transport belongs only to the API. `docs/phase-4-operations.md` records executed validation
and approval evidence while `quickstart.md` remains the reusable procedure. No project or permanent
process is added.

## Complexity Tracking

Not applicable: the pre-research and post-design constitution checks pass without exceptions.
