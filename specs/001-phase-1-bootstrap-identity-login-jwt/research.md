# Phase 0 Research: Phase 1 Bootstrap, Identity, Admin, Login and JWT

All decisions below apply only to Roadmap Phase 1. They derive from the SRS, Technical
Constraints, Roadmap §7/G1, the constitution, and the approved feature specification.

## Platform and Architecture

**Decision**: Use .NET 10 / `net10.0`, stable C# 14, ASP.NET Core 10 Minimal APIs, and the four
baseline product projects with feature vertical slices. Domain remains empty of Identity models;
Application owns only current-use ports; Infrastructure owns framework adapters; Api composes.

**Rationale**: This is the mandated baseline and preserves hexagonal dependency direction without
inventing a domain model around ASP.NET Core Identity.

**Alternatives considered**: Controllers, horizontal service/repository layers, MediatR/CQRS,
generic repositories, or Identity types in Domain were rejected by the technical baseline.

## Current-Use Application Boundaries

**Decision**: Define two narrow Application ports used by the Login slice: one for Identity-backed
credential validation and one for access-token issuance. Keep migration/bootstrap as an
Infrastructure initializer invoked by Api rather than adding a speculative Application port.

**Rationale**: These are actual technology boundaries required by the current use case. They keep
Application independent from Identity and JWT implementations with minimal ceremony.

**Alternatives considered**: Direct framework access from the endpoint would bypass Application;
ports for refresh, email, key rotation, future administration, or generic persistence have no
Phase 1 consumer and were rejected.

## Identity Model and Password Policy

**Decision**: Use native `IdentityUser<string>`, `IdentityRole<string>`, and Identity EF stores.
Do not add a custom `Enabled` property, must-change-password flag, or Domain identity wrapper.
Enforce normalized email uniqueness in the database. Configure Identity password-policy defaults
to permit the mandatory initial password `admin` while keeping policy values externally
configurable; Identity still performs all hashing and verification.

**Rationale**: Identity supplies the current fields and security behavior. Disable management is
introduced in Phase 3, password change in Phase 5, and hardening in Phase 7. The explicit initial
credential must be creatable without bypassing Identity. Phase 1 satisfies the replacement notice
through operational guidance rather than adding future-only account state.

**Alternatives considered**: A future-only `Enabled` column, custom password hashing, or bypassing
Identity password validation were rejected. Applying a stronger incompatible default would make
the SRS-mandated initial credential impossible.

## Idempotent Bootstrap Identity

**Decision**: Give the built-in administrator and role fixed stable IDs. After migrations, use a
transaction to create the fixed role, user, and assignment as one bootstrap operation only when
the fixed user does not exist. If that user exists, do not rewrite email, password, role assignment,
or other mutable state.

**Rationale**: Fixed IDs identify the built-in records even after mutable names or email change.
A transaction prevents partial initialization while respecting the requirement that restart must
not restore or overwrite later changes.

**Alternatives considered**: Re-querying only by original email/name could create duplicates after
legitimate edits. Reasserting seed values or role membership each startup would overwrite changes.
A separate bootstrap-marker table adds state that fixed IDs and a transaction make unnecessary.

## Database Initialization and Failure Model

**Decision**: Validate required configuration and RSA material, run EF Core `MigrateAsync`, then
run bootstrap before starting normal request processing. Invalid configuration, migration failure,
or bootstrap failure terminates startup and therefore never announces readiness. Once initialized,
readiness returns `503` if SQLite later becomes unavailable.

**Rationale**: Fail-fast startup is the smallest model satisfying automatic migrations,
idempotency, and “not ready on initialization failure.” It avoids a partially operational host.

**Alternatives considered**: `EnsureCreated`, Compose `dotnet ef`, migration/bootstrap containers,
manual scripts, and a limited half-started host were rejected as unnecessary or prohibited.

## Login and Account Enumeration Resistance

**Decision**: Normalize and find email through Identity, verify passwords with Identity failure
accounting enabled, and perform Identity-hasher-equivalent password work for an unknown email.
Return one identical generic `401` ProblemDetails representation for unknown email, wrong password,
locked account, and disabled account once that state exists.

**Rationale**: This preserves Identity lockout accounting and the SRS anti-enumeration contract
without exposing failure causes.

**Alternatives considered**: Early return for unknown accounts, custom password verification, or
distinct messages/extensions were rejected. Full rate limiting, timing-distribution testing, and
final lockout configuration remain Phase 7.

## JWT Issuance and Time

**Decision**: Load one externally configured RSA private PEM, sign with RS256 using a pinned
Microsoft IdentityModel issuance library, and use `System.TimeProvider`. Emit `sub` from the stable
Identity user ID, current email, one `role` claim per role, configured `iss`/`aud`, UTC NumericDate
`iat`/`exp`, and unique `jti`. Default lifetime is exactly 15 minutes and is configurable. Return
only `accessToken` and `expiresAtUtc`.

**Rationale**: This is the SRS contract and supports deterministic tests while keeping private
signing ability exclusive to Authentication API.

**Alternatives considered**: Symmetric signing, custom JWT/cryptography, generated ephemeral keys,
custom `IClock`, refresh data, JWKS, automatic rotation, Vault/KMS/HSM, and premature JwtBearer
consumer validation were rejected.

## HTTP and Health Contracts

**Decision**: Expose only login, liveness, and readiness. Use explicit small request/response
mapping and ProblemDetails errors. Health responses contain only `{ "status": "healthy" }` on
success; readiness uses a safe generic `503` after an initialized host loses database availability.
The OpenAPI 3.1 file is the planning contract; runtime OpenAPI/Scalar setup remains Phase 8.

**Rationale**: Exact contracts remove planning ambiguity without installing future-phase
documentation infrastructure or leaking configuration and storage details.

**Alternatives considered**: Future endpoints, detailed health component output, custom error
formats, Swashbuckle/NSwag, or interactive documentation in Phase 1 were rejected.

## Persistence and Container Lifecycle

**Decision**: Use one official-provider SQLite file, reference path `/app/data/auth.db`, and one
private PEM path under `/app/keys`, backed by explicit host bind mounts. The key mount is read-only
where supported; the application runs non-root. A Compose-external volume is allowed only when
managed independently from the project.

**Rationale**: Host-managed state survives restart, rebuild, and `docker compose down -v` and
matches the reference baseline without another service.

**Alternatives considered**: Container filesystem storage, Compose-managed named volumes as the
only copy, another database server, multiple DbContexts, and storage/bootstrap containers were
rejected.

## Dependency Timing

**Decision**: Add only Identity EF, official EF SQLite, development-only EF Design, one Microsoft
IdentityModel issuance library, xUnit v3/Microsoft Testing Platform, and Mvc.Testing. Use built-in
DI, logging, health checks, and ProblemDetails. Defer OpenAPI/Scalar runtime packages, persistent
file logging, JwtBearer consumer validation, Data Protection persistence, MailKit, NSubstitute,
and all prohibited packages.

**Rationale**: Every Phase 1 dependency has an immediate consumer; deferred dependencies belong
to later roadmap capabilities.

**Alternatives considered**: Installing the entire final dependency set up front was rejected by
YAGNI and roadmap sequencing.

## Verification Strategy

**Decision**: Use four consolidated integration scenarios plus a disposable Compose lifecycle
demonstration. Use real SQLite, a controlled clock, and test RSA material. Inspect Compose and
artifacts for prohibited services, commands, embedded secrets, and storage choices.

**Rationale**: This covers Phase 1 critical behavior and G1 with high-value evidence and avoids
duplicating framework checks across layers.

**Alternatives considered**: EF Core InMemory, mocked Identity/JWT, sleeps, one test per assertion,
coverage targets, API A/B validation, and mandatory Docker execution in the normal `dotnet test`
suite were rejected. Phase 8 repeats complete production-equivalent teardown acceptance.

## Resolved Baseline Tensions

- SRS final login includes refresh, while Roadmap Phase 4 introduces refresh; Phase 1 returns
  access token and expiry only.
- The SRS controls the configurable 15-minute default despite “recommended” summary wording.
- Final conceptual refresh/session and Data Protection state are excluded until Phases 4 and 6.
- Phase 1 demonstrates only current SQLite/Identity/RSA survival; Phase 8 repeats final teardown
  acceptance with the complete topology and all later persistent assets.
- Disabled-account behavior remains part of the generic contract when that state exists, but
  Phase 1 adds no future-only disable field or management behavior.

No unresolved clarification remains.
