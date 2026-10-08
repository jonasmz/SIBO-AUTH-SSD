# Implementation Plan: Phase 7 — Security Hardening

**Branch**: `007-phase-7-security-hardening` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-phase-7-security-hardening/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 7, Gate G7 (Roadmap §13)

## Summary

Harden the existing authentication surface without adding endpoints or changing contracts.
Identity lockout gets explicit initial values (5 failures, 15 minutes) that the existing
`Identity` configuration section can still override. The four anonymous sensitive endpoints get
independent per-origin fixed-window policies through the ASP.NET Core rate-limiting middleware
(shared framework, no package), with documented conservative defaults, external overrides and a
`429` ProblemDetails rejection. Forgot-password gets a second, in-memory limit per Identity-
normalized address, checked in the endpoint after validation. `UseForwardedHeaders` honors
`X-Forwarded-For`/`X-Forwarded-Proto` only from explicitly configured proxies or networks (the
framework's loopback defaults are cleared), so the effective client address used by limits and logs
cannot be forged. New structured events record the real cause of each login failure, a lockout
being applied, and a limit being applied. Cookie, origin, CORS and anti-enumeration behavior from
Phases 1–6 is verified, not redesigned. A reference Nginx configuration (first limiting layer plus
overwritten forwarded headers) is documented and exercised once by a disposable, acceptance-only
proxy container. No migration, no new package, no new permanent service.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`, SDK pinned by `global.json`), stable C# 14

**Primary Dependencies**: ASP.NET Core 10.0.12 Minimal APIs; ASP.NET Core Identity 10.0.12
(`LockoutOptions`, `AccessFailedAsync`, `IsLockedOutAsync`, `ILookupNormalizer`);
`Microsoft.AspNetCore.RateLimiting` and `System.Threading.RateLimiting` (shared framework);
`Microsoft.AspNetCore.HttpOverrides` (`ForwardedHeadersOptions.KnownProxies`/`KnownIPNetworks`,
shared framework). **New packages**: none. Acceptance only: official `nginx` Alpine image, pinned
tag and digest, in a test-only Compose override.

**Storage**: Existing SQLite database, no schema change (lockout columns already exist on
`AspNetUsers`). Request-limit counters in process memory only (SRS NFR-SEC-BF-010).

**Testing**: xUnit.net v3 on Microsoft Testing Platform; `WebApplicationFactory` with real
Identity/SQLite; a test-only startup filter sets the connecting address per request so per-origin
and forwarded-header behavior is observable in `TestServer`; lockout expiry driven by moving the
persisted `LockoutEnd` (Identity 10 reads the wall clock, not `TimeProvider`); window exhaustion
tested without waits; `CapturingLoggerProvider` for event and secret assertions;
`tests/acceptance/phase-7.sh` with `compose.reference-proxy.yml`, chaining Phase 6–1 regression.

**Target Platform**: Linux containers from official .NET 10 images, non-root; Compose topology
unchanged (`auth-api`, `api-a`, `api-b`); only optional environment entries added to `auth-api`.

**Project Type**: Internal REST web service (Authentication API) plus unchanged reference consumer.

**Performance Goals**: None invented. Limit evaluation is an in-memory partition lookup per request.

**Constraints**: Lockout and limits independent (FR-006); unchanged in-limit responses (FR-005);
no forwarded header honored without configuration (FR-008); no secrets in responses/logs
(FR-016/017); no Redis, distributed or database counters, gateway, or new logging framework
(FR-018, NFR-003); single-instance counters reset on restart (spec Assumptions).

**Scale/Scope**: Four rate-limit policies plus one address limiter, one forwarded-headers
registration, lockout defaults, three event families, one test-only startup filter, reference
proxy configuration, acceptance override and script, Gate G7 documentation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline authority and traceability | PASS | PASS | Decisions trace to SRS NFR-SEC-BF-001–013, NFR-NET-001–003, NFR-CSRF-001–004, NFR-CORS-001–003, NFR-SEC-ENUM-001–005, NFR-LOG-001–004, NFR-CONFIG-002/003, TEST-044/045; Technical Constraints §14.4, §15, §37; Roadmap §13/G7. Limit defaults are recorded as a project decision ([research.md §2](research.md)), not as normative values. The deterministic-window reconciliation (NFR-002) is recorded in [research.md §4](research.md). |
| II. Incremental vertical capabilities | PASS | PASS | Only Roadmap §13 scope. No endpoint, session semantics, frontend, TLS, file logger or production Nginx (Phase 8). The proxy container exists only in the acceptance override. |
| III. Hexagonal boundaries and feature slices | PASS | PASS | Domain and Application untouched. Infrastructure: lockout defaults and lockout/login events beside the Identity adapters that already call `AccessFailedAsync`. Api (composition root): HTTP-only concerns — forwarded headers, rate-limit policies, `429` writer, recovery address limiter used by the forgot-password endpoint. `Program.cs` gains only registration and middleware lines. |
| IV. Deliberate simplicity and dependency control | PASS | PASS | Framework middleware for limits and forwarded headers; no package, no custom limiter algorithm, no store, no abstraction port. Three small settings types and one limiter wrapper with a current consumer each. |
| V. Security by construction | PASS | PASS | Lockout is Identity's own; no custom counter. Effective address only from trusted hops. `429` reveals no account data. Events carry user IDs, causes and addresses, never passwords, tokens, cookies, reset tokens, keys or SMTP credentials. Invalid limit or proxy settings fail fast. Cookie and origin controls unchanged. |
| VI. Tests of implemented behavior | PASS | PASS | [quickstart.md](quickstart.md): consolidated integration scenarios on real Identity/SQLite, deterministic lockout expiry, no sleeps in the automated suite, host-level configuration tests (defaults, overrides, fail-fast; the Api project is the composition root and is already referenced only by integration tests), Compose acceptance with forged headers and the reference proxy, Phase 1–6 regression. |
| VII. Persistence ownership and deployment integrity | PASS | PASS | No schema or storage change; counters in memory; forwarded headers trusted only from authorized proxies/networks; proxy protection does not replace lockout or application limits; `compose.yml` keeps three services. |

No constitution violation requires a complexity exception.

## Design

### Request pipeline (`Program.cs`)

```text
UseForwardedHeaders      # trusted hops only; first, so everything sees the effective address
UseExceptionHandler
UseRateLimiter           # endpoint policies; rejection → 429 ProblemDetails + event
UseAuthentication / UseAuthorization
endpoints                # login/refresh/forgot/reset carry .RequireRateLimiting(policy)
```

A rejected request never reaches the handler, so it neither reads nor changes lockout state
(FR-006). On refresh and logout the origin check stays inside the endpoint, before the cookie.

### Lockout (FR-001/002)

`AddIdentityCore` setup sets `Lockout.MaxFailedAccessAttempts = 5`,
`Lockout.DefaultLockoutTimeSpan = 15 min`, `Lockout.AllowedForNewUsers = true`; the existing
`Configure<IdentityOptions>(configuration.GetSection("Identity"))` runs afterwards and overrides
them (`Identity__Lockout__MaxFailedAccessAttempts`, `Identity__Lockout__DefaultLockoutTimeSpan`).
Startup validation fails fast on a non-positive count or span. `IdentityCredentialValidator` keeps
its control flow; it only logs the cause and, after `AccessFailedAsync`, logs `AccountLockedOut`
when the account became locked. `PasswordChange` emits the same lockout event.

### Rate limiting (FR-003/004/007/019)

- Policies `login`, `refresh`, `forgot-password`, `reset-password`: fixed window, `QueueLimit = 0`,
  partitioned by the effective client address (IPv4-mapped IPv6 folded to IPv4; a missing address
  falls into one shared partition). Values from `RateLimiting:<Policy>:PermitLimit` /
  `WindowSeconds`, defaults in [contracts/configuration-and-proxy.md](contracts/configuration-and-proxy.md).
- `OnRejected`: `429` ProblemDetails (`Too Many Requests`), `Retry-After` when the limiter reports
  it, and a `RateLimitApplied` event with policy and effective address.
- Forgot-password address limit: singleton `RecoveryAddressLimiter` over a
  `PartitionedRateLimiter<string>` keyed by `ILookupNormalizer.NormalizeEmail`, checked after `400`
  validation and before the readiness check; rejection uses the same `429` writer and event (policy
  `forgot-password-address`, no address logged).

### Forwarded headers (FR-008/009/010)

`ForwardedHeaders = XForwardedFor | XForwardedProto`; `KnownProxies` and `KnownIPNetworks` are
cleared and refilled only from `ReverseProxy:TrustedProxies` / `ReverseProxy:TrustedNetworks`
(comma-separated); `ForwardLimit = null` so a chain is consumed only while each hop is trusted.
`X-Forwarded-Host` is not honored. Empty configuration → no forwarded header is honored.

### Files

```text
src/Authentication.Api/Security/ForwardedHeadersRegistration.cs        # new: options parsing + registration
src/Authentication.Api/Security/ReverseProxyOptions.cs                 # new
src/Authentication.Api/Security/RateLimitingRegistration.cs            # new: policies, partition key, OnRejected
src/Authentication.Api/Security/RateLimitingOptions.cs                 # new: five policy settings + defaults
src/Authentication.Api/Security/RateLimitPolicy.cs                     # new: one policy's permit/window value
src/Authentication.Api/Security/TooManyRequests.cs                     # new: shared 429 ProblemDetails + event
src/Authentication.Api/Features/PasswordRecovery/RecoveryAddressLimiter.cs  # new
src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs  # policy + address limit
src/Authentication.Api/Features/Login/LoginEndpoint.cs                 # .RequireRateLimiting
src/Authentication.Api/Features/Sessions/RefreshEndpoint.cs            # .RequireRateLimiting
src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs   # .RequireRateLimiting
src/Authentication.Api/Program.cs                                      # registrations + middleware
src/Authentication.Api/appsettings.json                                # RateLimiting / ReverseProxy placeholders
src/Authentication.Infrastructure/DependencyInjection.cs               # lockout defaults + validation
src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs   # LoginFailed / AccountLockedOut events
src/Authentication.Infrastructure/Identity/PasswordChange.cs           # AccountLockedOut event
compose.yml                                                            # optional AUTH_RATE_LIMIT_* / AUTH_TRUSTED_* env
.env.example                                                           # Phase 7 section
docs/reference-proxy/nginx.conf                                        # new: reference first layer + headers
docs/phase-7-operations.md                                             # new: settings, proxy contract, Gate G7 evidence
tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs   # lifted default limits, address filter
tests/Authentication.IntegrationTests/Infrastructure/TestConnectionAddressStartupFilter.cs  # new, test-only
tests/Authentication.IntegrationTests/Infrastructure/CountingPasswordHasher.cs    # new, test-only hasher decorator
tests/Authentication.IntegrationTests/Scenarios/AccountLockoutTests.cs            # new
tests/Authentication.IntegrationTests/Scenarios/RateLimitingTests.cs              # new
tests/Authentication.IntegrationTests/Scenarios/ForwardedHeadersTests.cs          # new
tests/Authentication.IntegrationTests/Scenarios/BrowserBoundaryTests.cs           # new: cookie, origin, no CORS
tests/Authentication.IntegrationTests/Scenarios/AntiEnumerationTests.cs           # new
tests/Authentication.IntegrationTests/Scenarios/SecretExposureTests.cs            # new: consolidated scan
tests/Authentication.IntegrationTests/Scenarios/SecurityConfigurationTests.cs     # new: defaults, overrides, fail-fast
tests/acceptance/compose.reference-proxy.yml                                      # new, acceptance only
tests/acceptance/phase-7.sh                                                       # new
tests/acceptance/phase-{1..6}.sh                                                  # export generous AUTH_RATE_LIMIT_* for regression
```

## Project Structure

### Documentation (this feature)

```text
specs/007-phase-7-security-hardening/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── authentication-api-rate-limits.openapi.yaml
│   └── configuration-and-proxy.md
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
│   ├── DependencyInjection.cs                 # lockout defaults
│   └── Identity/                              # login/lockout events
├── Authentication.Api/
│   ├── Security/                              # forwarded headers, rate limiting, 429
│   └── Features/
│       ├── Login/ Sessions/                   # policy attachment
│       └── PasswordRecovery/                  # policies + address limiter
└── ReferenceConsumer.Api/                     # unchanged

docs/
├── reference-proxy/nginx.conf
└── phase-7-operations.md

tests/
├── Authentication.IntegrationTests/{Infrastructure,Scenarios}/
└── acceptance/
    ├── compose.reference-proxy.yml
    └── phase-7.sh
```

**Structure Decision**: Keep the four-project hexagonal solution. Rate limiting and forwarded
headers are HTTP boundary concerns of the composition root, so they live in a new
`Authentication.Api/Security` folder (cross-endpoint) while the address limiter stays in the
`PasswordRecovery` slice that is its only consumer. Lockout configuration and its events stay with
the Identity adapters in Infrastructure. The proxy is an operator artifact plus a test-only override.

## Complexity Tracking

Not applicable: the pre-research and post-design constitution checks pass without exceptions.
