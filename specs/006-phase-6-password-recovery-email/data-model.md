# Data Model: Password Recovery, Reset and Email

Phase 6 adds **no table, column, index or migration**. The reset token is not stored; the only new
persistent state is the Data Protection key ring, which lives on the filesystem.

## Reset token (not persisted)

| Aspect | Rule |
|---|---|
| Issuer | `UserManager.GeneratePasswordResetTokenAsync` via `DataProtectorTokenProvider` (`Default`) |
| Content | Data-protected: creation time, user id, purpose `ResetPassword`, security stamp |
| Lifetime | Identity default (`TokenLifespan`, one day); no new setting |
| Transport | Base64url (UTF-8) of the Identity token, only in the recovery email body |
| Valid while | Not expired, unaltered, same user, security stamp unchanged, key ring available |
| Invalidated by | Successful reset or password change (stamp rotation), expiry, loss of the key ring |
| Never in | Responses, logs, database, URLs |

## Key ring (filesystem)

| Aspect | Rule |
|---|---|
| Location | `DataProtection:KeysPath` (required); Compose: `/var/lib/auth-api/dataprotection` bind-mounted from `AUTH_DATAPROTECTION_HOST_PATH`; reference host path `/srv/auth-system/dataprotection` |
| Application name | `Authentication.Api` (fixed; changing it invalidates outstanding tokens) |
| Startup | Directory must exist and be writable, otherwise startup fails naming only the setting |
| Permissions | Owner = container user (`APP_UID`), mode `0700`; operator access only |
| Lifecycle | Independent of Compose; survives restart, rebuild, recreation, `docker compose down -v` |
| At rest | Unencrypted XML files protected by filesystem permissions (documented) |

## Existing entity: ApplicationUser (Identity)

| Field | Change by a successful reset |
|---|---|
| `PasswordHash` | Replaced by `ResetPasswordAsync`. |
| `SecurityStamp` | Rotated by Identity (invalidates this and every other outstanding reset token). |
| `ConcurrencyStamp` | Rotated by Identity's `UpdateAsync`. |
| `Email`, `UserName`, `IsEnabled`, roles, `AccessFailedCount`, `LockoutEnd` | Unchanged. |

A recovery request changes no user field. A failed reset changes no field.

## Existing entity: RenewableSessionFamily (Phase 4)

On a successful reset every family with `IsActive(now)` gets `RevokedAtUtc = now` and
`RevocationReason = PasswordReset`. No family is kept. Already revoked or expired families are
untouched. `RefreshCredential` rows are not modified.

### Enumeration: SessionRevocationReason

`Replay = 0`, `Logout = 1`, `UserDisabled = 2`, `Administrator = 3`, `PasswordChanged = 4`,
**`PasswordReset = 5`** (appended; stored as `int?`; no schema change).

## State transitions

```text
forgot-password (anonymous)
  ├─ invalid body ──────────────────────────────► 400
  ├─ not ready / DbException ───────────────────► 503
  └─ FindByEmailAsync
       ├─ unknown or disabled ──────────────────► 204   (no token, no email, no log event)
       └─ enabled ─► GeneratePasswordResetToken ─► log PasswordResetRequested
                     └─ IEmailSender.SendAsync ─┬─ delivered ───────────► 204
                                                └─ failed (logged) ─────► 204

reset-password (anonymous)
  ├─ invalid body ──────────────────────────────► 400
  ├─ not ready ─────────────────────────────────► 503
  └─ BEGIN serializable
       ├─ unknown / disabled / undecodable token ► rollback → 401
       ├─ ResetPasswordAsync
       │    ├─ InvalidToken ────────────────────► rollback → 401
       │    ├─ Password* ───────────────────────► rollback → 400 (policy)
       │    └─ other failure ───────────────────► rollback → 400 (invalid request)
       ├─ revoke all active families (PasswordReset)
       ├─ COMMIT ── DbException/DbUpdateException ► rollback → 503
       └─ log PasswordReset ────────────────────► 204
```

## Application-layer types (feature `PasswordRecovery`)

| Type | Shape | Notes |
|---|---|---|
| `IEmailSender` | `Task<bool> SendAsync(EmailMessage, CancellationToken)` | `false` = not delivered (already logged by the adapter) |
| `EmailMessage` | `ToAddress`, `Subject`, `TextBody` | Plain text only |
| `IPasswordRecovery` | `Task<PasswordResetTicket?> IssueResetTokenAsync(string email, CancellationToken)`; `Task<ResetPasswordOutcome> ResetAsync(ResetPasswordCommand, CancellationToken)` | Implemented by `Infrastructure/Identity/PasswordRecovery` |
| `PasswordResetTicket` | `Email`, `Token` (already base64url) | Only for enabled accounts |
| `ForgotPasswordHandler` | `Task HandleAsync(string email, CancellationToken)` | Issues, composes the message, sends; never reports delivery |
| `ResetPasswordCommand` | `Email`, `Token`, `NewPassword` | Not logged |
| `ResetPasswordOutcome` | `Reset`, `InvalidToken`, `InvalidNewPassword`, `Invalid` | |

## Infrastructure configuration types

| Type | Fields | Validation (startup, fail fast, values never echoed) |
|---|---|---|
| `SmtpOptions` | `Host`, `Port`, `Security`, `Username`, `Password`, `SenderAddress`, `SenderName` | Per [research.md §5](research.md) |
| `DataProtectionStorageOptions` | `KeysPath` | Exists and writable |

## Validation rules

| Rule | Owner |
|---|---|
| JSON body; non-blank fields; valid email format | API request types (`LoginRequest` convention) |
| Email normalization | Identity `FindByEmailAsync` (same as login and administration) |
| Token validity | Identity (`ResetPasswordAsync` / `DataProtectorTokenProvider`) |
| New password policy | Identity password validators (`Identity` configuration section) |
| Eligibility | Enabled account only; lockout ignored |
