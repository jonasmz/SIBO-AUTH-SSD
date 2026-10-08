# Phase 6 Operations

Phase 6 adds email-based password recovery to Authentication API: `POST /api/auth/forgot-password`
and `POST /api/auth/reset-password`, SMTP delivery behind an application port, and a persistent
Data Protection key ring. There is no schema change or migration, and no new permanent service,
queue, retry mechanism, template engine, or token store: the same `auth-api`, `api-a`, and `api-b`
Compose services run as in Phase 5, and `auth-api` gains one more bind mount and its SMTP settings.

## New required configuration

`auth-api` refuses to start, naming only the setting and never its value, when any of these is
missing or invalid.

| Variable (Compose) | Setting | Rule |
|---|---|---|
| `AUTH_SMTP_HOST` | `Smtp:Host` | Non-blank |
| `AUTH_SMTP_PORT` | `Smtp:Port` | Integer 1–65535 |
| `AUTH_SMTP_SECURITY` | `Smtp:Security` | `None`, `StartTls`, or `SslOnConnect`. There is no automatic mode: an opportunistic mode could silently fall back to plaintext, so the operator chooses explicitly |
| `AUTH_SMTP_USERNAME` / `AUTH_SMTP_PASSWORD` | `Smtp:Username` / `Smtp:Password` | Both set, or both empty |
| `AUTH_SMTP_SENDER_ADDRESS` | `Smtp:SenderAddress` | Valid email address |
| `AUTH_SMTP_SENDER_NAME` | `Smtp:SenderName` | Non-blank |
| `AUTH_DATAPROTECTION_HOST_PATH` | `DataProtection:KeysPath` (inside the container) | An existing, writable directory |

SMTP credentials come only from external configuration (environment or secret mechanism of your
deployment). They are never baked into the image, returned in a response, or written to a log.

## Data Protection key ring

Reset tokens are protected by ASP.NET Core Data Protection. The key ring that verifies them must
outlive the container **and** the Compose project, otherwise every outstanding token becomes
invalid on a rebuild.

- **Reference deployment**: a host bind mount. Create the directory, owned by the container user
  (`APP_UID`, UID of the image's non-root `app` user), mode `0700`, and set
  `AUTH_DATAPROTECTION_HOST_PATH` to it, for example:

  ```bash
  sudo install -d -m 0700 -o "$APP_UID" /srv/auth-system/dataprotection
  ```

- **Alternative**: an `external: true` Compose volume created and managed outside the project. Do
  not use a named volume that Compose itself creates; `docker compose down -v` would delete it.
- **What survives**: restart, rebuild, container recreation, and `docker compose down -v`. A token
  that is still within its validity (Identity's default one day) keeps working across all of them.
- **What does not**: losing or replacing the directory invalidates outstanding reset tokens (users
  simply request a new one). Back it up with the same care as the SQLite database.
- **At rest**: the XML keys are not encrypted; the filesystem permissions are the protection. ASP.NET
  Core logs a startup warning about the missing key encryptor; it is expected. Only Authentication
  API and the authorized operator should be able to write the directory.
- **Fixed application name**: keys are bound to the application name `Authentication.Api`.
  Changing it invalidates outstanding tokens.

## Endpoints

| Operation | Success | Other outcomes |
|---|---|---|
| `POST /api/auth/forgot-password` (`email`) | `204`, always the same for a well-formed request | `400 The request is invalid.`; `503 The service is not ready.` |
| `POST /api/auth/reset-password` (`email`, `token`, `newPassword`) | `204` | `401 Invalid or expired reset token.` (invalid, altered, other-account, expired, or used token, unknown email, disabled account); `400` policy violation or invalid request; `503` |

Neither endpoint needs an access token, session, or `Origin`.

## Behavior to know

- **No account enumeration.** A well-formed forgot-password request answers the identical `204`
  (status, headers, body) for an existing, unknown, or disabled account, **and when email delivery
  fails**. Timing equivalence is not claimed.
- **Email delivery failures are only logged.** Because the caller must not learn that an account
  exists, a failed delivery is invisible to them. Operators see an `EmailDeliveryFailed` warning
  (exception type, SMTP status code, host, port, UTC time, trace id; never the recipient, subject,
  body, or credentials). Watch that event: otherwise users wait for a message that will not come.
- **The message** is plain text and carries the token on a line `Token: <value>` with the
  instruction to send it with their email and a new password to the reset endpoint. There is no
  link because no frontend exists.
- **Tokens are single-use and temporary.** Identity binds a token to the account's security stamp,
  which any password change or reset rotates, and applies its default lifetime of one day.
- **A reset ends every renewable session**, with none kept (unlike the authenticated change, which
  keeps the caller's session), in the same transaction as the password change. Access tokens
  already issued stay valid until they expire; there is no blacklist.
- **Disabled accounts cannot recover.** No token or email is produced for them, and a reset is
  rejected like an invalid token. Lockout state is not read or changed by recovery.
- **Throttling.** Rate limiting of these anonymous endpoints (SRS NFR-SEC-BF-006..009) is delivered
  in Phase 7; until then they are not throttled.
- **Logging.** Four events, all with the UTC time and trace/span ids:
  - `PasswordResetRequested` (`Information`): a token was issued; carries the user id.
  - `PasswordReset` (`Information`): a password was reset; carries the user id and the number of
    renewable session families revoked.
  - `EmailDeliveryFailed` (`Warning`): the recovery email was not delivered; carries the exception
    type, the SMTP status code when the server rejected a command, and the configured host and port.
    The caller still received the generic `204`.
  - `PasswordResetTokenFailed` (`Warning`): Identity could not issue a token for an existing,
    enabled account (for example a missing security stamp or an unusable key ring); carries the
    exception type only. The caller still received the generic `204`, and no email was sent.

  Tokens, passwords, hashes, security stamps, recipient addresses, message bodies, exception
  messages, and SMTP credentials never reach the logs. A warning's trace id links it to the request
  and, for a delivery failure, to the `PasswordResetRequested` event that names the user.

## Verification commands

```bash
dotnet build --no-incremental
dotnet test                         # unit + integration (real Identity, SQLite and Data Protection)
tests/acceptance/phase-6.sh         # Compose lifecycle with a disposable SMTP sink; also runs Phase 5, 4, 3, 2 and 1 acceptance
```

Requires `docker compose`, `openssl`, `curl`, `jq`, and Docker access to pull the pinned mail sink
image. The sink (`tests/acceptance/compose.mail-sink.yml`, Mailpit pinned by digest) is an
acceptance-only override loaded through `COMPOSE_FILE`; `compose.yml` never references it. The
script prepares the key-ring directory as the container user with mode `0700` through a throwaway
root container, because that directory is private to the container user. The reusable validation
guide is `specs/006-phase-6-password-recovery-email/quickstart.md`.

## Gate G6 evidence (recorded 2026-10-08, refreshed after the convergence tasks T043–T050)

| State | Evidence | Result |
|---|---|---|
| Build | `dotnet build --no-incremental` | PASS, 0 warnings, 0 errors |
| Tests | `dotnet test` | PASS, 157 of 157 (unit and integration), 0 skipped |
| Startup | `phase-6.sh`: stack ready on disposable storage and an SMTP sink; ready again after `restart`, `up --force-recreate`, and `down -v` + `up` | PASS |
| Feature | `phase-6.sh`: identical `204` for existing and unknown addresses, one SMTP delivery to the existing account only, token valid after the lifecycle operations, reset replaces the password, every session revoked, token single-use, key ring owned by the container UID with mode `0700`, secret-free logs with the recovery events | PASS |
| Regression | `phase-6.sh` finishing with `phase-5.sh` → `phase-4.sh` → `phase-3.sh` → `phase-2.sh` → `phase-1.sh` | PASS (all report ALL PASS) |

Decoupled SMTP and scope boundaries:

- `grep -rn "MailKit\|MimeKit" src/Authentication.Domain src/Authentication.Application --include='*.cs' --include='*.csproj'`
  returns nothing: MailKit is referenced only by Infrastructure.
- The only package added is MailKit 4.18.0 (`Directory.Packages.props` and the Infrastructure project).
- `compose.yml` still defines exactly `auth-api`, `api-a`, and `api-b`; it gained the key-ring bind mount
  and the `Smtp__*`/`DataProtection__*` settings only.
- No queue, background sender, retry mechanism, template engine, token store, rate limiter, or
  distributed cache exists in `src`; no migration was added (`dotnet ef migrations
  has-pending-model-changes` reports none).

Focused tests live in `tests/Authentication.IntegrationTests/Scenarios/`:
`PasswordRecoveryConfigurationTests`, `PasswordRecoveryRequestTests`,
`PasswordRecoveryDeliveryFailureTests`, `PasswordResetTests`, `PasswordResetConcurrencyTests`,
`PasswordResetSessionRevocationTests`, `PasswordRecoveryRestartTests`, and
`ConsumerValidationTests`; unit tests are `SmtpOptionsTests` and `SmtpEmailSenderMessageTests`.

Gate G6 approval by the project owner is **pending** (task T042).
