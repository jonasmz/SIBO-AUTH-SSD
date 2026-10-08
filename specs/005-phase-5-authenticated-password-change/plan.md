# Implementation Plan: Phase 5 — Authenticated Password Change

**Branch**: `005-phase-5-authenticated-password-change` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-phase-5-authenticated-password-change/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 5, Gate G5 (Roadmap §11)

## Summary

Add `POST /api/auth/change-password` for any authenticated user. The endpoint takes the caller's
identity only from the access token `sub`, validates a JSON body with `currentPassword` and
`newPassword`, and calls one Infrastructure adapter. Inside a single serializable SQLite
transaction the adapter calls Identity's `ChangePasswordAsync` (current-password verification,
configured policy, hash and security-stamp update) and revokes every active renewable-session
family of the user with the new reason `PasswordChanged`, except the family identified by a usable
`auth_refresh` cookie owned by the same user. Success (`204`) is returned only after commit; any
failure rolls back both. No migration, no new package, no email infrastructure, and no change to
JWT issuance or consumer validation. The existing bootstrap already skips an existing
administrator, so a changed `admin` password survives restart; Phase 5 proves it.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`, SDK 10.0.112 via `global.json`), stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs with the existing JwtBearer authentication
(`JwtValidationRegistration`); ASP.NET Core Identity 10.0.12 (`UserManager.ChangePasswordAsync`);
EF Core SQLite 10.0.12. No new NuGet dependency.

**Storage**: Existing single SQLite database and `AuthenticationDbContext`. No schema change:
`SessionRevocationReason` gains an appended value stored in the existing `int?` column.

**Testing**: xUnit.net v3 on Microsoft Testing Platform; `WebApplicationFactory`
(`AuthenticationApiFactory`) with real Identity, Minimal APIs and SQLite; temporary SQLite file for
the restart scenario; `ControlledTimeProvider` for expiry; existing `ReferenceConsumerFactory` for
local JWT validation; `tests/acceptance/phase-5.sh` on Compose plus Phase 1–4 scripts.

**Target Platform**: Linux containers from official .NET 10 images, non-root; existing Compose
topology (`auth-api`, `api-a`, `api-b`) unchanged.

**Project Type**: Internal REST web service (Authentication API) plus unchanged reference consumer.

**Performance Goals**: None invented. One user lookup, at most one indexed credential lookup and
one indexed family query per request.

**Constraints**: Change and revocation atomic (FR-010); failures change nothing (FR-009); cookie
only selects the kept family (FR-017); no blacklist or remote validation (FR-011); no secrets in
responses or logs (FR-014/015); an incorrect current password is counted through Identity's
`AccessFailedAsync` (NFR-SEC-BF-001), with no added lockout check or throttling (Phase 7).

**Scale/Scope**: One endpoint, one Application port with command/outcome, one Infrastructure
adapter, one enum value, one log event, tests, one acceptance script and Gate G5 documentation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline authority and traceability | PASS | PASS | Decisions trace to SRS §17 FR-CHANGE-PWD-001–005, FR-ADMIN-BOOT-003, NFR-SEC-ADMIN-001–003, NFR-SEC-BF-001, FR-LOGOUT-005–007, FR-USER-005, NFR-LOG-002, §34 error table; Technical Constraints §8 (Identity responsibilities); Roadmap §11/G5. Status codes are settled by the SRS §34 table ([research.md §2](research.md)), not invented. |
| II. Incremental vertical capabilities | PASS | PASS | Only Roadmap §11.2 scope. Reuses Phase 4 families and cookie. No SMTP, `IEmailSender`, forgot/reset, reset token, MFA, session listing or rate limiting. |
| III. Hexagonal boundaries and feature slices | PASS | PASS | Domain: enum value only, existing `Revoke`/`IsActive`. Application: `Features/Passwords` port, command, outcome (no Identity types). Infrastructure: `Identity/PasswordChange` owns Identity + EF transaction. API: `Features/Passwords` endpoint, request, cookie read, ProblemDetails mapping. `Program.cs` only mounts the endpoint. |
| IV. Deliberate simplicity and dependency control | PASS | PASS | No package, migration, handler, repository or new service. One port with one current consumer. |
| V. Security by construction | PASS | PASS | Identity verifies, validates and hashes; security stamp handled by Identity. Account target from `sub` only. Generic details, no Identity descriptions. RS256/consumer validation untouched. Log event carries no secrets. Bearer-only authorization makes Origin checks unnecessary ([research.md §6](research.md)). |
| VI. Tests of implemented behavior | PASS | PASS | [quickstart.md](quickstart.md) lists the consolidated integration scenarios on real SQLite/Identity: controlled time, concurrency, trigger-injected rollback faults, restart on a temp file, log scanning, acceptance and Phase 1–4 regression. |
| VII. Persistence ownership and deployment integrity | PASS | PASS | Same Auth-owned database and startup path; no schema change; idempotent bootstrap preserves the changed administrator password (verified by restart tests). |

No constitution violation requires a complexity exception.

## Design

### Request flow

1. `MapPost("/api/auth/change-password").RequireAuthorization()` — the default policy uses the
   existing JwtBearer scheme, so missing/invalid tokens get the established `401` challenge.
2. Read and validate `ChangePasswordRequest` exactly as `LoginEndpoint` reads `LoginRequest`
   (JSON content type, `JsonException` → `400`).
3. `InitializationState.IsReady` else `503`.
4. `sub` from `HttpContext.User` (`MapInboundClaims = false`); absent → `401 Invalid credentials.`
5. `RefreshCredentialProtector.TryHash(request.Cookies["auth_refresh"])` → optional digest.
6. `IPasswordChange.ChangeAsync(command)`; map outcome per [research.md §2](research.md);
   `DbException`/`DbUpdateException` → `503`.

### Adapter (`Infrastructure/Identity/PasswordChange`)

Serializable transaction → `FindByIdAsync(sub)` → resolve kept family (rules in
[data-model.md](data-model.md)) → `ChangePasswordAsync` → map Identity errors (`PasswordMismatch`
→ `AccessFailedAsync`, commit the counter only, `InvalidCurrentPassword`; `Password*` →
`InvalidNewPassword`; else `Invalid`) and return without commit on those other failures → load the user's non-revoked families, revoke those `IsActive(now)` and not kept
with `PasswordChanged` → `SaveChangesAsync` → commit → `LoggerMessage` event.

### Files

```text
src/Authentication.Domain/Sessions/SessionRevocationReason.cs              # + PasswordChanged
src/Authentication.Application/Features/Passwords/IPasswordChange.cs        # new
src/Authentication.Application/Features/Passwords/ChangePasswordCommand.cs  # new
src/Authentication.Application/Features/Passwords/ChangePasswordOutcome.cs  # new
src/Authentication.Infrastructure/Identity/PasswordChange.cs                # new
src/Authentication.Infrastructure/DependencyInjection.cs                    # register IPasswordChange
src/Authentication.Api/Features/Passwords/ChangePasswordEndpoint.cs         # new
src/Authentication.Api/Features/Passwords/ChangePasswordRequest.cs          # new
src/Authentication.Api/Program.cs                                           # MapChangePasswordEndpoint()
tests/Authentication.IntegrationTests/Scenarios/PasswordChange*Tests.cs     # new: core, concurrency, session revocation, administrator
tests/acceptance/phase-5.sh                                                 # new
docs/phase-5-operations.md                                                  # new: retire-`admin` procedure (NFR-SEC-ADMIN-001) + Gate G5 evidence
```

## Project Structure

### Documentation (this feature)

```text
specs/005-phase-5-authenticated-password-change/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── authentication-api-password.openapi.yaml
├── checklists/
│   └── requirements.md
└── tasks.md                                       # generated by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── Authentication.Domain/
│   └── Sessions/                    # enum value appended
├── Authentication.Application/
│   └── Features/
│       └── Passwords/               # port, command, outcome
├── Authentication.Infrastructure/
│   └── Identity/                    # PasswordChange adapter
├── Authentication.Api/
│   └── Features/
│       └── Passwords/               # endpoint and request
└── ReferenceConsumer.Api/           # unchanged

tests/
├── Authentication.IntegrationTests/
│   └── Scenarios/                   # Phase 5 scenarios
└── acceptance/
    └── phase-5.sh
```

**Structure Decision**: Keep the four-project hexagonal solution with a new `Passwords` vertical
slice in Application and API; the Identity-bound adapter sits with the other Identity adapters.
No project, migration or permanent process is added.

## Complexity Tracking

Not applicable: the pre-research and post-design constitution checks pass without exceptions.
