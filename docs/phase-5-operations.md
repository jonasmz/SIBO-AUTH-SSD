# Phase 5 Operations

Phase 5 adds authenticated self-service password change to Authentication API. There is no new
service, volume, storage, schema change, migration, package, or setting: the same `auth-api`,
`api-a`, and `api-b` Compose services run as in Phase 4, and no email infrastructure exists.

## Retire the initial `admin` password (required after first access)

The built-in administrator is created once with `admin@local.invalid` / `admin` (SRS
NFR-SEC-ADMIN-001). Replace that password after the first sign-in:

```bash
BASE=http://localhost:8080
TOKEN=$(curl -s -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid","password":"admin"}' $BASE/api/auth/login | jq -r .accessToken)

curl -i -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"currentPassword":"admin","newPassword":"<your new secret>"}' \
  $BASE/api/auth/change-password
# HTTP/1.1 204 No Content
```

From then on `admin` no longer signs in and the new secret does. Restarting, rebuilding, or
redeploying never restores `admin`: startup initialization skips an administrator that already
exists (SRS NFR-SEC-ADMIN-003). No email address or SMTP is involved; the same endpoint serves every
authenticated user.

## Endpoint behavior

`POST /api/auth/change-password` acts only on the account in the access token `sub`; the body has
no account identifier. The new password must satisfy the externally configured Identity policy.

| Outcome | Response |
|---|---|
| Changed | `204`, no body, no cookie |
| Missing, invalid, or expired access token | `401` with `WWW-Authenticate: Bearer` |
| Incorrect current password | `401 Invalid credentials.` (no `WWW-Authenticate`) |
| Not JSON, malformed, or missing/blank field | `400 The request is invalid.` |
| New password violates the policy | `400 The password does not satisfy the password policy.` |
| Persistence unavailable | `503 The service is not ready.` |

Any refusal changes neither the password nor any session.

## Behavior to know

- **Other sessions end.** In the same transaction as the change, every active renewable-session
  family of the user is revoked (reason `PasswordChanged`) except the one identified by the
  `auth_refresh` cookie sent with the request, when that credential is usable and belongs to the
  same user. Without such a cookie (for example a non-browser client) every family is revoked.
  The cookie only selects the session to keep; it never authorizes the request and is not modified.
- **Access tokens stay stateless.** A token issued before the change remains valid at Authentication
  API and at the consumers until it expires (15 minutes by default); there is no blacklist.
- **No `Origin` check.** The operation is authorized by the `Authorization: Bearer` header, which
  browsers never attach automatically, so it is not reachable by cross-site requests.
- **No throttling.** A wrong current password does not count toward Identity lockout. Rate limiting
  belongs to Phase 7.
- **Logging.** One `Information` event per change records the user id, number of revoked families,
  whether a session was kept, the UTC time, and trace/span ids. Passwords, hashes, security stamps,
  tokens, and cookie values never reach the logs.

## Verification commands

```bash
dotnet build --no-incremental
dotnet test                         # unit + integration (real Identity and SQLite)
tests/acceptance/phase-5.sh         # Compose lifecycle; also runs Phase 4, 3, 2 and 1 acceptance
```

Requires `docker compose`, `openssl`, `curl`, `jq`. The reusable validation guide is
`specs/005-phase-5-authenticated-password-change/quickstart.md`.

## Gate G5 evidence (recorded 2026-10-08)

| State | Evidence | Result |
|---|---|---|
| Build | `dotnet build --no-incremental` | PASS, 0 warnings, 0 errors |
| Tests | `dotnet test` | PASS, 115 of 115 (unit and integration), 0 skipped |
| Startup | `phase-5.sh`: Compose stack ready on disposable storage; restart keeps the new secret | PASS |
| Feature | `phase-5.sh`: rejections, initial administrator password replaced, other sessions revoked while the current one renews, ordinary user change, secret-free logs with the change event | PASS |
| Regression | `phase-5.sh` finishing with `phase-4.sh` → `phase-3.sh` → `phase-2.sh` → `phase-1.sh` | PASS (all report ALL PASS) |

No premature email or recovery infrastructure:

- `grep -rn -E 'IEmailSender|MailKit|SmtpClient|forgot|reset-password' src` returns nothing.
- No NuGet package, Compose service, migration, or configuration setting was added; the model has no
  pending changes (`dotnet ef migrations has-pending-model-changes`).

Focused tests live in `tests/Authentication.IntegrationTests/Scenarios/`:
`PasswordChangeTests`, `PasswordChangeConcurrencyTests`, `PasswordChangeSessionRevocationTests`,
`AdministratorPasswordChangeTests`, and `ConsumerValidationTests`.

Gate G5 approval by the project owner is **pending** (task T024).
