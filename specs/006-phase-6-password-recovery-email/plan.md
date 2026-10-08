# Implementation Plan: Phase 6 — Password Recovery, Reset and Email

**Branch**: `006-phase-6-password-recovery-email` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-phase-6-password-recovery-email/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 6, Gate G6 (Roadmap §12)

## Summary

Add anonymous `POST /api/auth/forgot-password` and `POST /api/auth/reset-password`. Forgot looks up
the address through Identity and, only for an enabled account, generates an Identity
data-protected reset token and sends one plain-text email through a new Application port
`IEmailSender`, implemented by a MailKit SMTP adapter in Infrastructure. Every well-formed request
gets the same `204`, including when delivery fails (the failure is only logged safely). Reset runs
in one serializable SQLite transaction: Identity's `ResetPasswordAsync`, then revocation of every
active renewable-session family with the new reason `PasswordReset`. Unknown email, disabled account
and every invalid token get one identical `401`. The Data Protection key ring is persisted to a
required, externally configured directory bind-mounted from the host, so outstanding tokens survive
restart, recreation and `docker compose down -v`. No migration, no token store, no queue and no new
permanent service.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`, SDK 10.0.112 via `global.json`), stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs; ASP.NET Core Identity 10.0.12
(`DataProtectorTokenProvider`, `GeneratePasswordResetTokenAsync`, `ResetPasswordAsync`); ASP.NET
Core Data Protection (shared framework, `PersistKeysToFileSystem`); EF Core SQLite 10.0.12.
**New**: `MailKit` 4.18.0 (Infrastructure only; brings MimeKit 4.18.0) — justified by Technical
Constraints §16.2 and Roadmap §12.2.

**Storage**: Existing SQLite database, no schema change (`SessionRevocationReason` gains an
appended value in the existing `int?` column). New filesystem key ring at `DataProtection:KeysPath`.

**Testing**: xUnit.net v3 on Microsoft Testing Platform; `WebApplicationFactory` with real Identity,
SQLite and Data Protection; capturing fake `IEmailSender` as the only test double (email boundary,
TC §22.4); real `SmtpEmailSender` against a refused local port for the failure path; negative
`TokenLifespan` for deterministic expiry; temporary SQLite file and key directory for restart;
`tests/acceptance/phase-6.sh` with a test-only Mailpit override, chaining Phase 5–1 regression.

**Target Platform**: Linux containers from official .NET 10 images, non-root (`APP_UID`); Compose
topology unchanged except one more bind mount and SMTP environment variables on `auth-api`.

**Project Type**: Internal REST web service (Authentication API) plus unchanged reference consumer.

**Performance Goals**: None invented. Forgot-password performs one indexed user lookup and at most
one synchronous SMTP exchange; response timing equivalence is explicitly not claimed (spec).

**Constraints**: Anti-enumeration on status, headers and body (FR-002/008); atomic reset and
revocation (FR-010); no token, password, stamp or SMTP secret in responses or logs (FR-014/017);
no throttling — SRS NFR-SEC-BF-006–009 for forgot-password are implemented in Phase 7 per the spec's
phase-boundary note; no queue, retries, background sending, templates, token store or extra
service (NFR-003).

**Scale/Scope**: Two endpoints, one Application slice (two ports, one handler, commands/outcomes),
two Infrastructure adapters plus two options types, one shared revocation helper, one enum value,
three log events, Compose/acceptance updates, Gate G6 documentation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline authority and traceability | PASS | PASS | Decisions trace to SRS §16 FR-PWD-001–012, NFR-MAIL-001–005, NFR-DP-001–007, NFR-LOG-001–004, NFR-CONFIG-002/003, NFR-ERR-001–003, §34.1; Technical Constraints §8, §16, §22.4, §30, §31, §38; Roadmap §12/G6. The forgot-password rate-limit timing conflict is carried by the spec's phase-boundary note. A Phase 5 divergence from SRS NFR-SEC-BF-001 found during planning was corrected in Phase 5 before implementation ([research.md §13](research.md)). |
| II. Incremental vertical capabilities | PASS | PASS | Only Roadmap §12 scope: email port + SMTP, persistent Data Protection, forgot, reset. No rate limiting, frontend, MFA, email verification or admin resets. The mail sink exists only in the acceptance override. |
| III. Hexagonal boundaries and feature slices | PASS | PASS | Domain: enum value only. Application `Features/PasswordRecovery`: `IEmailSender`, `IPasswordRecovery`, `ForgotPasswordHandler` (message content), commands/outcomes — no Identity, MailKit or HTTP types. Infrastructure: `Identity/PasswordRecovery`, `Email/SmtpEmailSender`, options, Data Protection registration, `Sessions/SessionFamilyRevocation` helper. API `Features/PasswordRecovery`: endpoints and requests. MailKit is referenced by Infrastructure only. |
| IV. Deliberate simplicity and dependency control | PASS | PASS | One new package mandated by the baseline (MailKit, pinned 4.18.0, ≥ 2 weeks old, no advisories). Only the Data Protection token provider is registered. A third revocation copy is replaced by one helper instead of added. No queue, retry, template engine, token store, repository or extra container in `compose.yml`. |
| V. Security by construction | PASS | PASS | Identity generates and validates tokens and hashes passwords; stamp rotation makes tokens single-use. Identical `204`/`401` responses. Credentials only from external configuration, validated and never echoed; no MailKit protocol logging; plaintext-fallback modes excluded. Key ring outside the image with restricted permissions. JWT model untouched. |
| VI. Tests of implemented behavior | PASS | PASS | [quickstart.md](quickstart.md): 20 consolidated integration scenarios on real Identity/SQLite/Data Protection, one fake only at the email boundary, deterministic expiry and refused-port failure, trigger-injected faults, restart with and without the same key ring, unit tests for options and MIME, Compose lifecycle acceptance including `down -v`, Phase 1–5 regression. |
| VII. Persistence ownership and deployment integrity | PASS | PASS | Key ring on a host bind mount independent of Compose volumes, configurable and documented, writable only by Auth API and the operator; SQLite unchanged; startup still migrates before readiness; no extra service in production topology. |

No constitution violation requires a complexity exception.

## Design

### Forgot-password flow

1. `MapPost("/api/auth/forgot-password").AllowAnonymous()`; read/validate `ForgotPasswordRequest` as
   `LoginEndpoint` does → `400`; `InitializationState.IsReady` else `503`.
2. `ForgotPasswordHandler.HandleAsync(email)`: `IPasswordRecovery.IssueResetTokenAsync` → `null`
   for unknown/disabled; otherwise compose `EmailMessage` and `IEmailSender.SendAsync`.
3. Always `204`; `DbException`/`DbUpdateException` → `503`.

### Reset-password flow

1. `MapPost("/api/auth/reset-password").AllowAnonymous()`; validate `ResetPasswordRequest` → `400`;
   readiness → `503`.
2. `IPasswordRecovery.ResetAsync` (transaction per [data-model.md](data-model.md)); map
   `Reset` → `204`, `InvalidToken` → `401 Invalid or expired reset token.`, `InvalidNewPassword` →
   `400` policy detail, `Invalid` → `400`; database exceptions → `503`.

### Files

```text
src/Authentication.Domain/Sessions/SessionRevocationReason.cs                     # + PasswordReset
src/Authentication.Application/Features/PasswordRecovery/IEmailSender.cs          # new
src/Authentication.Application/Features/PasswordRecovery/EmailMessage.cs          # new
src/Authentication.Application/Features/PasswordRecovery/IPasswordRecovery.cs     # new
src/Authentication.Application/Features/PasswordRecovery/PasswordResetTicket.cs   # new
src/Authentication.Application/Features/PasswordRecovery/ForgotPasswordHandler.cs # new
src/Authentication.Application/Features/PasswordRecovery/ResetPasswordCommand.cs  # new
src/Authentication.Application/Features/PasswordRecovery/ResetPasswordOutcome.cs  # new
src/Authentication.Infrastructure/Identity/PasswordRecovery.cs                    # new
src/Authentication.Infrastructure/Email/SmtpEmailSender.cs                        # new (MailKit)
src/Authentication.Infrastructure/Email/SmtpOptions.cs                            # new
src/Authentication.Infrastructure/Security/DataProtectionStorageOptions.cs        # new
src/Authentication.Infrastructure/Sessions/SessionFamilyRevocation.cs             # new helper
src/Authentication.Infrastructure/Identity/UserAdministration.cs                  # use helper
src/Authentication.Infrastructure/Identity/PasswordChange.cs                      # use helper
src/Authentication.Infrastructure/DependencyInjection.cs                          # options, DP, token provider, ports
src/Authentication.Infrastructure/Authentication.Infrastructure.csproj            # + MailKit
src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs        # new
src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordRequest.cs         # new
src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs         # new
src/Authentication.Api/Features/PasswordRecovery/ResetPasswordRequest.cs          # new
src/Authentication.Api/Program.cs                                                 # map endpoints
src/Authentication.Api/appsettings.json                                           # empty Smtp/DataProtection placeholders
Directory.Packages.props                                                          # MailKit 4.18.0
compose.yml                                                                       # DP bind mount, Smtp__* env
tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs  # DP path, Smtp settings, fake sender
tests/Authentication.IntegrationTests/Infrastructure/CapturingEmailSender.cs      # new fake
tests/Authentication.IntegrationTests/Scenarios/PasswordRecovery*Tests.cs         # new
tests/Authentication.UnitTests/Infrastructure/SmtpOptionsTests.cs                 # new
tests/Authentication.UnitTests/Infrastructure/SmtpEmailSenderMessageTests.cs      # new
tests/acceptance/compose.mail-sink.yml                                            # new, acceptance only
tests/acceptance/phase-6.sh                                                       # new
tests/acceptance/phase-{1..5}.sh                                                  # export disposable SMTP/DP settings
docs/phase-6-operations.md                                                        # new: SMTP + key-ring procedure, Gate G6 evidence
```

## Project Structure

### Documentation (this feature)

```text
specs/006-phase-6-password-recovery-email/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── authentication-api-password-recovery.openapi.yaml
├── checklists/
│   └── requirements.md
└── tasks.md                                   # generated by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── Authentication.Domain/
│   └── Sessions/                        # enum value appended
├── Authentication.Application/
│   └── Features/
│       └── PasswordRecovery/            # ports, handler, commands, outcomes
├── Authentication.Infrastructure/
│   ├── Email/                           # MailKit adapter + options
│   ├── Identity/                        # PasswordRecovery adapter
│   ├── Security/                        # key-ring options
│   └── Sessions/                        # shared revocation helper
├── Authentication.Api/
│   └── Features/
│       └── PasswordRecovery/            # endpoints and requests
└── ReferenceConsumer.Api/               # unchanged

tests/
├── Authentication.UnitTests/Infrastructure/
├── Authentication.IntegrationTests/{Infrastructure,Scenarios}/
└── acceptance/
    ├── compose.mail-sink.yml
    └── phase-6.sh
```

**Structure Decision**: Keep the four-project hexagonal solution with a new `PasswordRecovery`
slice in Application and API. Provider and Identity specifics stay in Infrastructure. The only new
infrastructure is a host directory for the key ring; the mail sink is test-only.

## Complexity Tracking

Not applicable: the pre-research and post-design constitution checks pass without exceptions.
