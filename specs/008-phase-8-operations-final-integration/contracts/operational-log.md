# Contract: Operational Log

## Settings

| Key (env form) | Default | Rule |
|---|---|---|
| `Logging__File__Directory` | none (Compose: `/app/logs`) | required; must exist and be writable, else startup fails naming only the setting |
| `Logging__File__RetentionDays` | `30` | integer ≥ 1, else startup fails naming only the setting |
| `Logging__File__LogLevel__*` | inherits `Logging:LogLevel` | standard provider filter (alias `File`) |

Compose maps `AUTH_LOG_RETENTION_DAYS` (optional, default `30`) to the retention.

## Files

- Name `auth-yyyy-MM-dd.log`, UTC date of the events it holds; UTF-8 without BOM; one event per line.
- On startup and on each UTC date change, files named `auth-yyyy-MM-dd.log` older than
  `today − RetentionDays` are deleted; other files are never touched.

## Line

```text
2026-10-08T14:03:12.457Z [Warning] Authentication.Infrastructure.Identity.IdentityCredentialValidator[1] trace=4bf92f3577b34da6a3ce929d0e0e4736 span=00f067aa0ba902b7 LoginFailed: reason WrongPassword, user 7c1… at 2026-10-08T14:03:12.4570000+00:00; trace 4bf9…, span 00f0….
```

`[EventId]`, `trace=`, `span=` are omitted when absent. Exceptions append ` exception=<Type>` and the
stack trace with newlines escaped as `\n`; the exception message is never written. Newlines inside a
message are escaped. The console shows the same events (simple console, single line, UTC timestamp).

## Security events (SRS NFR-LOG-002)

| Event (message prefix) | Phase | Status |
|---|---|---|
| `LoginSucceeded` | 8 | new |
| `LoginFailed` (reason) | 7 | existing |
| `AccountLockedOut` | 7 | existing |
| `RateLimitApplied` | 7 | existing |
| logout: `Renewable session family … revoked` | 4 | existing |
| `Password changed for user …` | 5 | existing |
| `PasswordReset` | 6 | existing |
| `UserCreated` | 8 | new |
| `UserEnabled` / `UserDisabled` | 8 | new |
| `UserRoleAssigned` / `UserRoleRemoved` | 8 | new |
| `Refresh credential replay detected …` | 4 | existing |
| `Renewable sessions of user … revoked` | 3–6 | existing |

Every event: UTC time and trace/span. Never: password, complete access or refresh token, refresh
credential or hash, reset token, private key, SMTP credential, other configuration secret, email
address in failure/limit events.
