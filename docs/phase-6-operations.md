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
- **Logging.** `PasswordResetRequested` and `PasswordReset` events carry the user id, UTC time, and
  trace/span ids (and the revoked count for a reset). Tokens, passwords, hashes, security stamps,
  and SMTP values never reach the logs.
