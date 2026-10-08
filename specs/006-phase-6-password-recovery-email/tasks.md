---

description: "Task list for Phase 6 password recovery, reset and email"
---

# Tasks: Phase 6 — Password Recovery, Reset and Email

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md), and
[authentication-api-password-recovery.openapi.yaml](contracts/authentication-api-password-recovery.openapi.yaml)

**Prerequisites**: Approved Phase 6 plan and clarified specification; Gate G5 closed (recorded in
the roadmap); the merged Phase 5 failed-attempt fix (PR #6) is already part of this branch.

**Tests**: Required. Use the existing xUnit v3 projects, `WebApplicationFactory`, real Identity,
SQLite, and Data Protection, and `ControlledTimeProvider`; the only test double is a capturing fake
at the `IEmailSender` boundary. No sleeps, EF InMemory, new test projects, or other mocks.
Scenarios are consolidated per [quickstart.md](quickstart.md) §2; do not add one test per
requirement.

**Organization**: Tasks are grouped by user story. Shared configuration, the email port, key-ring
persistence, and the revocation helper are completed first. Forgot-password (US1), reset (US2), and
session revocation (US3) build one adapter incrementally; US4 proves and documents key-ring
persistence.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites because it affects a distinct file.
- **[US#]**: The user story served by a task. Shared setup and foundation work has no story label.

## Phase 1: Setup and Configuration Baseline

**Purpose**: Introduce the one mandated package and the new external configuration without
changing any Phase 1–5 behavior.

- [X] T001 Add `MailKit` `4.18.0` (brings MimeKit 4.18.0) to `Directory.Packages.props` and reference it only from `src/Authentication.Infrastructure/Authentication.Infrastructure.csproj`; reference it from no other project.
- [X] T002 Add the required external configuration: empty `Smtp` (`Host`, `Port`, `Security`, `Username`, `Password`, `SenderAddress`, `SenderName`) and `DataProtection:KeysPath` placeholders in `src/Authentication.Api/appsettings.json`; in `compose.yml` add a host bind mount `${AUTH_DATAPROTECTION_HOST_PATH:?set AUTH_DATAPROTECTION_HOST_PATH}` to `/var/lib/auth-api/dataprotection` on `auth-api` only (no named or Compose-managed volume), set `DataProtection__KeysPath`, and pass `Smtp__*` from `AUTH_SMTP_*` variables; document runnable example values (reference host path `/srv/auth-system/dataprotection`) in `.env.example`; add no service.
- [X] T003 [P] Extend the reusable test host in `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs` and `tests/Authentication.IntegrationTests/Infrastructure/Phase1TestResources.cs`: a temporary key directory per resource set supplied as `DataProtection__KeysPath` (overridable to a different directory), dummy valid `Smtp__*` settings, an optional negative `DataProtectionTokenProviderOptions.TokenLifespan`, and a default capturing fake `IEmailSender` from a new `tests/Authentication.IntegrationTests/Infrastructure/CapturingEmailSender.cs` (records messages, returns `true`) with an option to keep the real sender instead.
- [X] T004 [P] Make the Phase 1–5 acceptance scripts keep working under the new required settings by exporting disposable `AUTH_DATAPROTECTION_HOST_PATH` (a directory under their `$STATE` created with `install -d -m 0777`, exactly like their existing disposable data directory, because the container runs as a different non-root UID; they do not verify key-ring permissions) and dummy `AUTH_SMTP_*` values in `tests/acceptance/phase-1.sh`, `tests/acceptance/phase-2.sh`, `tests/acceptance/phase-3.sh`, `tests/acceptance/phase-4.sh`, and `tests/acceptance/phase-5.sh`, defaulting each variable like the existing `AUTH_FRONTEND_ORIGIN` export so chained runs inherit or reset them consistently.

---

## Phase 2: Foundational Contracts and Infrastructure

**Purpose**: Add the revocation reason, the email and recovery ports, validated configuration,
persistent key ring, token provider, and the shared revocation helper that all stories use.

**⚠️ CRITICAL**: Complete this phase before implementing any user story.

- [X] T005 Append `PasswordReset = 5` to `SessionRevocationReason` in `src/Authentication.Domain/Sessions/SessionRevocationReason.cs`, keeping `Replay = 0`, `Logout = 1`, `UserDisabled = 2`, `Administrator = 3`, `PasswordChanged = 4` unchanged; no other Domain change.
- [X] T006 [P] Add the `PasswordRecovery` application slice under `src/Authentication.Application/Features/PasswordRecovery/`, one top-level type per matching file: `IEmailSender.cs` (`Task<bool> SendAsync(EmailMessage, CancellationToken)`, `false` = not delivered), `EmailMessage.cs` (`ToAddress`, `Subject`, `TextBody`, plain text only), `IPasswordRecovery.cs` (`Task<PasswordResetTicket?> IssueResetTokenAsync(string email, CancellationToken)` and `Task<ResetPasswordOutcome> ResetAsync(ResetPasswordCommand, CancellationToken)`), `PasswordResetTicket.cs` (`Email`, `Token` already base64url), `ResetPasswordCommand.cs` (`Email`, `Token`, `NewPassword`), and `ResetPasswordOutcome.cs` (`Reset`, `InvalidToken`, `InvalidNewPassword`, `Invalid`); no Identity, MailKit, or HTTP types.
- [X] T007 Define `SmtpOptions` with startup validation in `src/Authentication.Infrastructure/Email/SmtpOptions.cs`: `Host` non-blank; `Port` integer 1–65535; `Security` one of `None`, `StartTls`, `SslOnConnect` (no automatic or opportunistic mode); `Username` and `Password` both blank or both non-blank; `SenderAddress` a valid email address; `SenderName` non-blank; validation failures name only the setting and never echo a value.
- [X] T008 [P] Add unit coverage for every `SmtpOptions` rule, including partial credentials and an automatic-security value being rejected without leaking the value, in `tests/Authentication.UnitTests/Infrastructure/SmtpOptionsTests.cs`.
- [X] T009 Define `DataProtectionStorageOptions` with startup validation in `src/Authentication.Infrastructure/Security/DataProtectionStorageOptions.cs`: `KeysPath` is required, the directory must exist and be writable (create a probe file, then delete it), and failure names only `DataProtection:KeysPath` without echoing the path.
- [X] T010 Register the new infrastructure in `src/Authentication.Infrastructure/DependencyInjection.cs`: validated `SmtpOptions` and `DataProtectionStorageOptions` using the existing fail-fast `Require` pattern; `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(KeysPath)).SetApplicationName("Authentication.Api")`; only `DataProtectorTokenProvider<ApplicationUser>` registered under `TokenOptions.DefaultProvider` on the existing `AddIdentityCore` builder (not `AddDefaultTokenProviders()`), keeping Identity's default token lifetime and adding no lifetime setting.
- [X] T011 Extract the "revoke the user's active families" loop into one internal static helper in `src/Authentication.Infrastructure/Sessions/SessionFamilyRevocation.cs` (`RevokeActiveAsync(context, userId, now, reason, keepFamilyId?)`, runs inside the caller's transaction, returns the revoked count) and switch `src/Authentication.Infrastructure/Identity/UserAdministration.cs` and `src/Authentication.Infrastructure/Identity/PasswordChange.cs` to it with unchanged behavior and logs, proven by the existing Phase 3–5 tests; make it no repository or injected service.
- [X] T012 Verify Phase 6 needs no schema change by running `dotnet ef migrations has-pending-model-changes --project src/Authentication.Infrastructure --startup-project src/Authentication.Api` after T005 (supplying `Microsoft.EntityFrameworkCore.Design` temporarily from the local package cache and reverting that change, as in Phase 5), and confirm it reports none; do not add a migration.
- [X] T013 [P] Add startup configuration scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryConfigurationTests.cs`: missing, nonexistent, or unusable `DataProtection__KeysPath` (the unusable case points at an existing regular file rather than relying on directory permissions, so it also fails when the suite runs as root) and each invalid `Smtp__*` setting terminate startup naming only the setting, with no configured value echoed (quickstart scenario 19).

**Checkpoint**: The solution builds, existing behavior is unchanged, configuration fails fast without disclosing values, keys persist to the configured directory, and no schema change is required.

---

## Phase 3: User Story 1 - Password Recovery Request (Priority: P1) 🎯 MVP

**Goal**: An anonymous caller requests recovery by email; an existing, enabled account gets one
plain-text message carrying an Identity reset token, and every well-formed request receives the
identical `204`, including when delivery fails.

**Independent Test**: Request recovery for an enabled account, an unknown address, and a disabled
account; the responses are identical, one message is requested only for the enabled account, and no
response or log contains the token.

### Tests for User Story 1

- [X] T014 [P] [US1] Add forgot-password scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryRequestTests.cs`: enabled account, unknown address, and disabled account return the identical `204` (status, headers, empty body) with the fake sender called exactly once and only for the enabled account; the same address in a different letter case is treated as the enabled account; two consecutive requests for the enabled account produce two messages with no throttling; non-JSON, malformed, and missing, blank, or invalid email return `400 The request is invalid.` without a sender call; a recovery request revokes no session; and the captured token appears in no response header or body and in no captured log (quickstart scenarios 1–3, 5, and the forgot part of 20).
- [X] T015 [P] [US1] Add a real-adapter delivery-failure scenario in `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryDeliveryFailureTests.cs`: keep the real `SmtpEmailSender` pointed at a local port that was just bound and released so the connection is refused deterministically, and assert the identical `204`, one `EmailDeliveryFailed` warning carrying exception type, host, port, UTC time, and trace identifier, and no token, recipient, or SMTP password in the captured logs; and a token-generation failure for an enabled account (after startup, the key directory is replaced by a regular file so the key ring cannot be written) also returns the identical `204` with one safe `PasswordResetTokenFailed` warning, so a failure reachable only for existing accounts is not observable (quickstart scenarios 4 and 4b).
- [X] T016 [P] [US1] Add MIME construction unit coverage in `tests/Authentication.UnitTests/Infrastructure/SmtpEmailSenderMessageTests.cs`: configured sender address and display name, the recipient, the subject, and a plain-text body containing the token.

### Implementation for User Story 1

- [X] T017 [US1] Implement `src/Authentication.Infrastructure/Email/SmtpEmailSender.cs` as the only `IEmailSender` (MailKit confined to Infrastructure): a new `SmtpClient` per message with `ConnectAsync(host, port, security)`, `AuthenticateAsync` only when a username is configured, `SendAsync`, `DisconnectAsync(true)`, no protocol logger, the request `CancellationToken` passed through; catch every exception except `OperationCanceledException`, log one `Warning` `LoggerMessage` event `EmailDeliveryFailed` with exception type name, SMTP status code when it is a command exception, configured host and port, UTC time, and `Activity` trace/span identifiers, never the recipient, subject, body, username, or password, and return `false`.
- [X] T018 [US1] Implement `IssueResetTokenAsync` in `src/Authentication.Infrastructure/Identity/PasswordRecovery.cs`: `FindByEmailAsync` (Identity normalization, as login and administration), return `null` for an unknown or `!IsEnabled` account without any log event, otherwise `GeneratePasswordResetTokenAsync`; if it throws a non-database exception (for example `CryptographicException`, `IOException`, `UnauthorizedAccessException`), log one `Warning` `PasswordResetTokenFailed` event with the exception type name, UTC time, and trace/span identifiers and return `null`, so the caller still gets the generic `204` (database exceptions keep propagating to `503`, which is identical for every account); encode the token with `WebEncoders.Base64UrlEncode` (UTF-8), log one `Information` `PasswordResetRequested` event with the user id, UTC time, and trace/span identifiers (never the token), and return a `PasswordResetTicket`; change no user field.
- [X] T019 [US1] Add `ForgotPasswordHandler` in `src/Authentication.Application/Features/PasswordRecovery/ForgotPasswordHandler.cs`: call `IssueResetTokenAsync`, and for a ticket compose the single fixed plain-text `EmailMessage` (subject and body containing the token and telling the user to submit it with their email to `POST /api/auth/reset-password`, no link and no template engine) and call `IEmailSender.SendAsync`, ignoring the boolean result so delivery failure is never observable by the caller.
- [X] T020 [US1] Register `IEmailSender` → `SmtpEmailSender`, `IPasswordRecovery` → `PasswordRecovery`, and `ForgotPasswordHandler` as scoped services in `src/Authentication.Infrastructure/DependencyInjection.cs`.
- [X] T021 [P] [US1] Add `ForgotPasswordRequest` in `src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordRequest.cs` with `email` validated like `LoginRequest` (non-blank, `EmailAddressAttribute`).
- [X] T022 [US1] Add anonymous `POST /api/auth/forgot-password` in `src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs`: `AllowAnonymous()`; read and validate the body as `LoginEndpoint` does (invalid → `400 The request is invalid.` before any lookup, independent of account existence); `InitializationState.IsReady` else `503 The service is not ready.`; call the handler; always `204` with no body for a well-formed request; `DbException`/`DbUpdateException` → `503`.
- [X] T023 [US1] Map the forgot-password endpoint with `MapForgotPasswordEndpoint()` in `src/Authentication.Api/Program.cs` without changing the JWT, Origin, or CORS configuration.

**Checkpoint**: US1 is independently demonstrable without reset; delivery is decoupled behind the port and failures are invisible to callers.

---

## Phase 4: User Story 2 - Password Reset With a Token (Priority: P1)

**Goal**: A caller holding a valid token resets the password without any session; every invalid,
altered, other-account, expired, or used token, an unknown email, and a disabled account get one
identical `401` and change nothing.

**Independent Test**: Take the token from the fake sender, reset the password, and verify the old
password fails and the new one signs in, while each rejection case changes nothing.

### Tests for User Story 2

- [X] T024 [P] [US2] Add reset scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordResetTests.cs`: a valid token and valid password return `204` with no body or cookie, the old password logs in `401` and the new one `200`, and email, roles, enabled state, and lockout fields are unchanged; garbage, a one-character-altered token, another account's token, the same token reused after success, and a token issued before a Phase 5 password change return `401 Invalid or expired reset token.` without `WWW-Authenticate` and change nothing; an unknown email returns that same `401`; a genuinely valid token issued while the account was enabled is rejected with that same `401` after the account is disabled, and the password is unchanged; the reset email in a different letter case is accepted like the original address; the reset token presented as a `Bearer` access token or as the `auth_refresh` cookie is rejected (`401`) and grants nothing; an expired token (host with the negative `TokenLifespan` from T003, no waits) returns `401`; a valid token with a policy-violating password returns `400` with the policy detail and the token is still usable afterwards; non-JSON, malformed, and missing or blank fields return `400 The request is invalid.`; and no response or captured log contains a token, password, hash, or stamp (quickstart scenarios 6–11 and the reset part of 20).
- [X] T025 [P] [US2] Add a deterministic concurrent-reset scenario in `tests/Authentication.IntegrationTests/Scenarios/PasswordResetConcurrencyTests.cs`: two requests with one token issued concurrently with `Task.WhenAll` (no sleeps) yield exactly one `204` and one `401` whether or not they overlap, and only the winner's password authenticates (quickstart scenario 14).

### Implementation for User Story 2

- [X] T026 [US2] Implement `ResetAsync` in `src/Authentication.Infrastructure/Identity/PasswordRecovery.cs`: open an `IsolationLevel.Serializable` transaction; `FindByEmailAsync`; an unknown or `!IsEnabled` account, or a token that cannot be base64url-decoded, → `InvalidToken` with rollback; call `ResetPasswordAsync`; map Identity `InvalidToken` → `InvalidToken`, `Password*` codes → `InvalidNewPassword`, other failures → `Invalid` without leaking Identity descriptions, returning without commit on any failure; commit on success; read and change no lockout field and no attribute other than the credential and the Identity-rotated stamps.
- [X] T027 [P] [US2] Add `ResetPasswordRequest` in `src/Authentication.Api/Features/PasswordRecovery/ResetPasswordRequest.cs` with `email` (non-blank, valid format), `token`, and `newPassword` (non-blank), following the `LoginRequest` convention.
- [X] T028 [US2] Add anonymous `POST /api/auth/reset-password` in `src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs`: `AllowAnonymous()`, requiring no access token or session; validate the body (`400 The request is invalid.`); readiness else `503`; map `Reset` → `204` (no body, no cookie), `InvalidToken` → `401 Invalid or expired reset token.` (no `WWW-Authenticate`), `InvalidNewPassword` → `400 The password does not satisfy the password policy.`, `Invalid` → `400 The request is invalid.`, and `DbException`/`DbUpdateException` → `503`.
- [X] T029 [US2] Map the reset-password endpoint with `MapResetPasswordEndpoint()` in `src/Authentication.Api/Program.cs`.

**Checkpoint**: US2 restores access with a token and rejects every other case indistinguishably; sessions are still untouched until US3.

---

## Phase 5: User Story 3 - All Renewable Sessions End After a Reset (Priority: P1)

**Goal**: A successful reset revokes every active renewable-session family of the user in the same
transaction, keeps none, and a failed or partially failed reset revokes nothing.

**Independent Test**: Establish two renewable sessions, reset the password, and verify neither can
refresh; a failed reset leaves both usable.

### Tests for User Story 3

- [X] T030 [P] [US3] Add session-revocation scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordResetSessionRevocationTests.cs`: after a successful reset with two active families both refresh `401` and both rows are persisted revoked with reason `PasswordReset` (no family kept, another user's family untouched); each failed reset from T024 leaves both families refreshing; a password-write fault (temporary `RAISE(ABORT)` trigger on `AspNetUsers`, then dropped) yields `503` with the old password working, the token still usable, and sessions intact; a revocation-write fault (temporary trigger on `RenewableSessionFamilies`, then dropped) yields `503` with the old password working and both families still refreshing; and one captured `PasswordReset` event carries the user id, revoked count, UTC time, and trace identifier with no secret in any captured log (quickstart scenarios 12, 15, 16, and 20).

### Implementation for User Story 3

- [X] T031 [US3] Extend `ResetAsync` in `src/Authentication.Infrastructure/Identity/PasswordRecovery.cs` inside the existing transaction: after a successful `ResetPasswordAsync`, call `SessionFamilyRevocation.RevokeActiveAsync` with reason `SessionRevocationReason.PasswordReset` and no kept family, save, and commit so a failure at either step rolls back both the credential and the revocations; never modify a `RefreshCredential` row.
- [X] T032 [US3] Add the post-commit `Information` `PasswordReset` event in `src/Authentication.Infrastructure/Identity/PasswordRecovery.cs` using `LoggerMessage` with user id, revoked family count, UTC instant from `TimeProvider`, and `Activity` trace/span identifiers; log nothing on refusals and exclude tokens, passwords, hashes, stamps, and SMTP values.

**Checkpoint**: US3 completes the atomic reset-and-revoke behavior; access tokens stay stateless with no blacklist and no consumer change.

---

## Phase 6: User Story 4 - Recovery Survives Restarts and Redeployments (Priority: P2)

**Goal**: A still-valid reset token keeps working after restart, recreation, and teardown with
volumes, because the key ring lives on operator-controlled storage; a different key ring does not.

**Independent Test**: Issue a token, recreate the host on the same SQLite file and key directory,
and reset successfully; recreate it with a different key directory and verify rejection.

### Tests for User Story 4

- [X] T033 [P] [US4] Add restart scenarios in `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryRestartTests.cs`: a token issued by one host is accepted (`204`) by a second host created on the same temporary SQLite file and the same key directory (both hosts alive together, as in the Phase 5 administrator test), and a host created with a different key directory rejects the same token with `401` as a negative control (quickstart scenarios 17–18).

### Implementation for User Story 4

- [X] T034 [US4] Create `docs/phase-6-operations.md` with the operator procedure: the SMTP settings and their allowed values (secrets only from external configuration, no plaintext-fallback mode), the key-ring bind mount (`AUTH_DATAPROTECTION_HOST_PATH`, reference `/srv/auth-system/dataprotection`, owner = container user `APP_UID`, mode `0700`, never a Compose-managed volume, an `external` volume only as the documented alternative), that `docker compose down -v` and container recreation preserve it, that losing it invalidates outstanding tokens, that the XML keys are unencrypted at rest and protected by filesystem permissions, the fixed application name warning, the recovery endpoints with status codes from [research.md](research.md) §7, and the note that rate limiting of these endpoints is delivered in Phase 7; Gate G6 evidence is added in T038–T040.

**Checkpoint**: US4 shows key material outlives the container and Compose project, and is documented for operators.

---

## Phase 7: Gate G6 Verification and Cross-Cutting Evidence

**Purpose**: Demonstrate the completed vertical slice, the decoupled SMTP boundary, the Compose key-ring
lifecycle, local consumer validation, and all Phase 1–5 regression behavior.

- [X] T035 [P] Add the acceptance-only mail sink override in `tests/acceptance/compose.mail-sink.yml` using `axllent/mailpit:v1.31.1` pinned by digest `sha256:98b916bd3c8d61f7633a52d3ea2f58d00620cb01ca57ab59edde68c347a95365`, used only by the acceptance scripts and never referenced by `compose.yml`.
- [X] T036 Add the Phase 6 Compose acceptance lifecycle in `tests/acceptance/phase-6.sh`, modeled on `tests/acceptance/phase-5.sh` and using the T035 override with disposable host directories: request recovery for an existing account and an unknown address and compare the responses; read the token from Mailpit's HTTP API; establish two sessions; run `docker compose restart auth-api`, `up -d --force-recreate auth-api`, and `down -v` followed by `up` on the same host directories; reset with the token (`204`); prove both sessions refresh `401`, the new password logs in, the old one does not, and the token is rejected on reuse; before the first start, hand the key-ring directory to the container user without host root by running the auth-api image itself as root (`docker compose run --rm --no-deps --user 0 --entrypoint sh auth-api -c 'chown "$APP_UID" /var/lib/auth-api/dataprotection && chmod 0700 /var/lib/auth-api/dataprotection'`), then prove it is a host bind mount that is not a Compose volume, has mode `0700`, and is owned by the container UID (read with `docker compose run --rm --no-deps --entrypoint id auth-api -u`), and that key XML files were written there (listed through the same root container); remove it at teardown through the same root container so the disposable `$STATE` can be deleted; and finish by running `tests/acceptance/phase-5.sh` as regression (which runs Phases 4–1).
- [X] T037 Extend the log scan in `tests/acceptance/phase-6.sh` to assert the `auth-api` logs contain no reset token, old or new password, SMTP password, access token, refresh cookie value, or `PRIVATE KEY`, and that the `PasswordResetRequested` and `PasswordReset` events are present with UTC timestamps and trace identifiers.
- [X] T038 [P] Add consumer evidence in `tests/Authentication.IntegrationTests/Scenarios/ConsumerValidationTests.cs` that APIs A and B still accept an unexpired access token issued before a password reset, with no consumer-side change (quickstart scenario 13).
- [X] T039 Verify the architecture and scope boundaries and record the results in `docs/phase-6-operations.md`: `grep -rn MailKit src/Authentication.Domain src/Authentication.Application` returns nothing; `compose.yml` gained only the key-ring bind mount and `Smtp__*`/`DataProtection__*` settings and no service; no queue, scheduler, retry, template engine, token store, rate limiter, or additional package beyond MailKit exists.
- [X] T040 Run `dotnet build --no-incremental` and the complete unit/integration suite, resolve Phase 6 and Phase 1–5 regressions, and record the exact commands and PASS evidence in `docs/phase-6-operations.md`, keeping `specs/006-phase-6-password-recovery-email/quickstart.md` as the reusable validation guide.
- [X] T041 Run `tests/acceptance/phase-6.sh` and record all five Gate G6 evidence states—build, tests, startup, feature, and regression—in `docs/phase-6-operations.md`, linked to the focused test and acceptance output.
- [ ] T042 After T001-T041, review implementation and evidence against Roadmap §12.7, present `docs/phase-6-operations.md` to the project owner, and obtain explicit Gate G6 approval. Only after that approval, update the authorized Phase 6 checklist/status/progress records in `baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md` and `specs/006-phase-6-password-recovery-email/checklists/requirements.md`, record the approval date/evidence reference without changing normative requirements, and create the identifiable Gate G6 closing commit; never mark Phase 6 complete or create the closing commit before approval.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** has no implementation dependencies; T001 precedes T017, and T002 precedes T004's
  chained runs.
- **Phase 2**: T005 first; T006, T007, and T009 may run in parallel with it; T008 depends on T007;
  T010 depends on T001, T007, and T009; T011 is independent of T010; T012 depends on T005; T013
  depends on T007, T009, and T010.
- **US1** depends on Phases 1–2 (T017 needs T001/T007; T018 needs T006/T010; T022 needs T019/T021).
- **US2** depends on US1 because it extends the same adapter (`PasswordRecovery.cs`) and relies on
  the registrations and fake sender of US1.
- **US3** depends on US2 because it extends `ResetAsync` and needs T011.
- **US4** depends on Phase 2 and on US2 (a token must be redeemable); T034 may start once T010 exists.
- **Gate G6 verification** depends on all story phases.

### User Story Completion Order

```text
Setup → Foundation → US1 → US2 → US3 → US4 → Gate G6
```

### Parallel Opportunities

- T003/T004, T006/T007/T009, T008, T013, T014/T015/T016, T021, T024/T025, T027, T030, T033, T035,
  and T038 can run in parallel once their prerequisites are satisfied. T018, T026, T031, and T032
  share `PasswordRecovery.cs`, and the endpoint files and `Program.cs` mappings (T023, T029) share
  `Program.cs`, so those tasks run sequentially.

## Parallel Example: User Story 1

```text
Task: "Add forgot-password scenarios in tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryRequestTests.cs"
Task: "Add the delivery-failure scenario in tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryDeliveryFailureTests.cs"
Task: "Add MIME construction unit coverage in tests/Authentication.UnitTests/Infrastructure/SmtpEmailSenderMessageTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete setup and the foundational contracts, configuration, and key-ring persistence.
2. Complete US1 and prove recovery requests are anti-enumeration safe with decoupled delivery.
3. Validate US1 independently before adding reset.

### Incremental Delivery

1. US1 → recovery request and email delivery.
2. US2 → password reset with Identity tokens.
3. US3 → atomic revocation of every renewable session.
4. US4 → key-ring persistence proof and operator documentation.
5. Gate G6 → Compose lifecycle (restart, recreate, `down -v`) plus Phase 1–5 regressions.

## Notes

- Do not add rate limiting (Phase 7), a frontend, MFA, email verification, administrative resets,
  password history, a queue, retries, background sending, a template engine, a token store, a
  distributed cache, certificate-based key encryption, or any permanent service.
- Do not log or return reset tokens, passwords, hashes, stamps, SMTP credentials, recipient
  addresses, or message bodies; the mail sink exists only in the acceptance override.
- Do not change `plan.md`, the baseline documents, or any Phase 1–5 contract; the refactor in T011
  must leave Phase 3–5 behavior and logs identical.

---

## Phase 8: Convergence

- [X] T043 CRITICAL: Move `public enum SmtpSecurity` out of `src/Authentication.Infrastructure/Email/SmtpOptions.cs` into its own `src/Authentication.Infrastructure/Email/SmtpSecurity.cs` (file-scoped namespace, unchanged members and parsing), so each file holds one top-level type named like the file, per Constitution: Technical and Repository Constraints (contradicts)
- [X] T044 CRITICAL: Move `CapturingLoggerProvider` (and its nested logger) out of `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs` into its own `tests/Authentication.IntegrationTests/Infrastructure/CapturingLoggerProvider.cs` with unchanged behavior, per Constitution: Technical and Repository Constraints (contradicts)
- [X] T045 In `src/Authentication.Infrastructure/Identity/PasswordRecovery.cs`, wrap `GeneratePasswordResetTokenAsync` so a non-database exception (for example `CryptographicException`, `IOException`, `UnauthorizedAccessException`) logs one `Warning` `LoggerMessage` event `PasswordResetTokenFailed` with the exception type name, UTC time from `TimeProvider`, and `Activity` trace/span identifiers (no token, email, or exception message) and returns `null`, so forgot-password still answers the identical `204`; let `DbException`/`DbUpdateException` and `OperationCanceledException` propagate, per FR-002, FR-014 and research §6 (missing)
- [X] T046 Add the token-generation-failure scenario to `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryDeliveryFailureTests.cs`: make Identity's token generation fail for the enabled administrator by setting its `SecurityStamp` to `NULL` in SQLite (implementation note: replacing the key directory after startup does not work, because Data Protection creates and caches its key while the host starts), request recovery for the enabled administrator and for an unknown address, and assert both return the identical `204`, exactly one `PasswordResetTokenFailed` warning is captured with UTC time and trace identifier, no message reaches the sender, and no secret appears in the logs, per T015 and quickstart scenario 4b (missing)
- [X] T047 [P] Extend `tests/Authentication.IntegrationTests/Scenarios/PasswordResetTests.cs` so a valid token is redeemed with the account's email in a different letter case (`204`, new password logs in), per FR-004, T024 and quickstart scenario 8b (partial)
- [X] T048 [P] Extend `tests/Authentication.IntegrationTests/Scenarios/PasswordResetTests.cs` so a valid reset token presented as `Authorization: Bearer` to `/api/admin/users` and to API A, and as the `auth_refresh` cookie to `POST /api/auth/refresh`, is rejected with `401`, grants nothing, and remains redeemable afterwards, per spec edge case (single-purpose reset token), T024 and quickstart scenario 8b (partial)
- [X] T049 [P] In `tests/acceptance/phase-6.sh`, assert that the key-ring directory is owned by the container UID (`stat -c %u` equals `APP_UID_VALUE`) next to the existing mode `0700` check, per FR-016 and T036 (partial)
- [X] T050 [P] Update the logging section of `docs/phase-6-operations.md` to list the `Warning` events `EmailDeliveryFailed` (exception type, SMTP status, host, port, UTC, trace/span ids) and `PasswordResetTokenFailed` (exception type, UTC, trace/span ids) alongside `PasswordResetRequested` and `PasswordReset`, then re-run the suite and refresh the recorded Gate G6 evidence, per FR-017 and T034 (partial)

---

## Phase 9: Convergence

- [X] T051 [P] Extend `tests/Authentication.IntegrationTests/Scenarios/PasswordRecoveryConfigurationTests.cs` so an empty and a whitespace-only `DataProtection__KeysPath` also terminate startup naming only `DataProtection:KeysPath`, alongside the existing nonexistent-path and regular-file cases, per T013 and quickstart scenario 19 (partial)
