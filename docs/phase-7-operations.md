# Phase 7 Operations

Phase 7 hardens the existing authentication surface without adding endpoints, packages, schema, or
services: Identity account lockout with explicit initial values, independent per-origin request limits on
the four anonymous sensitive endpoints plus a per-address limit on password recovery, trusted
forwarded-header handling for the reverse-proxy boundary, and security events that name the real cause of a
failure. Cookie, origin, and anti-enumeration behavior from Phases 1–6 is verified, not redesigned.

## Settings (all optional)

Every setting has a default; blank keeps it. Invalid values terminate startup naming only the setting.

| Variable (Compose) | Setting | Default |
|---|---|---|
| `AUTH_LOCKOUT_MAX_FAILED_ATTEMPTS` | `Identity:Lockout:MaxFailedAccessAttempts` | `5` |
| `AUTH_LOCKOUT_DURATION` | `Identity:Lockout:DefaultLockoutTimeSpan` (`hh:mm:ss`) | `00:15:00` |
| `AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT` / `_WINDOW_SECONDS` | `RateLimiting:Login:*` | 10 per 60 s |
| `AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT` / `_WINDOW_SECONDS` | `RateLimiting:Refresh:*` | 30 per 60 s |
| `AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT` / `_WINDOW_SECONDS` | `RateLimiting:ForgotPassword:*` | 5 per 900 s |
| `AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT` / `_WINDOW_SECONDS` | `RateLimiting:ResetPassword:*` | 10 per 900 s |
| `AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT` / `_WINDOW_SECONDS` | `RateLimiting:ForgotPasswordAddress:*` | 3 per 3600 s |
| `AUTH_TRUSTED_PROXIES` | `ReverseProxy:TrustedProxies` (comma-separated IPs) | empty |
| `AUTH_TRUSTED_NETWORKS` | `ReverseProxy:TrustedNetworks` (comma-separated CIDR) | empty |

**About the request-limit figures.** The baseline requires the limits to exist, to be configurable, and to
answer `429`, but gives no numbers. The defaults above are a **project decision**, chosen conservatively for a
small internal user base behind one proxy; they are not normative. Review them for your traffic and override
them with the variables above. Limits are fixed windows per effective client address (and per normalized
address for the recovery limit), held in **process memory**: a restart resets them and several instances would
limit separately (the architecture runs one).

## Lockout

Identity locks an account after the configured number of consecutive failed password attempts for the
configured duration (default 5 attempts and 15 minutes; Identity's own default would be 5 minutes). A wrong
current password in `POST /api/auth/change-password` also counts. A locked account receives the same generic
`401` as any credential failure, a successful login resets the counter, and the lockout expires by itself.
Lockout is per account and independent of the request limits: neither reads or changes the other.

## Request limits

Exceeding a policy returns `429 Too Many Requests` as `application/problem+json` with a `Retry-After`
header, no account information, and before the request reaches credential checking (so a rejected request
never changes a lockout counter). Logout is not limited. The recovery limit counts every submitted address,
whether or not an account exists, so a `429` is never an existence signal.

## Reverse proxy and forwarded headers

- **Trust.** The service honors `X-Forwarded-For` and `X-Forwarded-Proto` only from the proxies and networks
  listed in `AUTH_TRUSTED_PROXIES` / `AUTH_TRUSTED_NETWORKS`. **With both empty (the default) forwarded-header
  processing is switched off entirely**: no header has any effect. (The framework middleware would otherwise
  trust every sender when its lists are empty.) `X-Forwarded-Host` is never honored.
- **Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** It makes the host register permissive options; the
  application replaces them, but the variable adds nothing and signals a misconfiguration.
- **Chains.** The address list is read from the right and stops at the first hop that is not authorized, so
  values a client wrote before it cannot choose the address used for limits and logs.
- **Obligations of the proxy.** Overwrite `X-Forwarded-For` with the address it saw (never append a
  client-supplied value); set `X-Forwarded-Proto` and forward `Host`; apply a first limiting layer to
  `/api/auth/login`, `/refresh`, `/forgot-password`, and `/reset-password`; limit request bodies; be the only
  route to `auth-api`. Its address or network is the only value placed in the trusted settings.
- **Reference configuration.** `docs/reference-proxy/nginx.conf` implements those obligations (60 requests per
  minute per client with a burst of 20, `429` on excess, 16 KB bodies). It is an operator artifact and is **not**
  part of `compose.yml`; the acceptance procedure runs it once in a disposable container
  (`tests/acceptance/compose.reference-proxy.yml`). The complete production proxy, frontend files, and TLS are
  delivered by Phase 8. The proxy layer never replaces Identity lockout or the application's own limits.

## Security events

| Event (level `Warning`) | Fields | Reveals |
|---|---|---|
| `LoginFailed` | `reason` (`UnknownAccount`, `WrongPassword`, `LockedOut`, `Disabled`), `user` id when known, UTC time, trace/span | the real cause for operators; never the email or password |
| `AccountLockedOut` | `user`, `lockoutEnd` (UTC), `source` (`Login` or `ChangePassword`), UTC time, trace/span | when and why a lock began |
| `RateLimitApplied` | `policy`, client address (omitted for the address policy), UTC time, trace/span | which limit fired for which origin; never an email |

HTTP responses stay generic regardless of the cause. Earlier events (replay, logout, password change and
reset, email delivery) are unchanged. Persistent file logging and collection belong to Phase 8.

## Verification commands

```bash
dotnet build --no-incremental -warnaserror
dotnet test                         # unit + integration (real Identity and SQLite)
tests/acceptance/phase-7.sh         # Compose lifecycle with a disposable reference proxy; also runs Phase 6, 5, 4, 3, 2 and 1 acceptance
```

Requires `docker compose`, `openssl`, `curl`, `jq`, and access to pull the pinned `nginx:stable-alpine` image.
No automated test waits for wall-clock time: lockout expiry is exercised by moving the persisted `LockoutEnd`
(Identity reads the system clock), and window renewal is demonstrated once by the acceptance script, which waits
only the `Retry-After` the service returned. The reusable validation guide is
`specs/007-phase-7-security-hardening/quickstart.md`.

## Gate G7 evidence (recorded 2026-10-08)

| State | Evidence | Result |
|---|---|---|
| Build | `dotnet build --no-incremental -warnaserror` | PASS, 0 warnings, 0 errors |
| Tests | `dotnet test` | PASS, 179 of 179 (unit and integration), 0 skipped |
| Startup | `phase-7.sh`: stack and reference proxy ready on disposable storage | PASS |
| Feature | `phase-7.sh`: five-failure lockout; application `429` per policy and for the recovery address, independent of each other and of the lockout; renewal after the returned `Retry-After`; forged `X-Forwarded-For` ignored directly and overwritten by the proxy; the proxy's own HTML `429`; lockout, login-failure, and rate-limit events with UTC time and trace and no secret or email in the logs | PASS |
| Regression | `phase-7.sh` finishing with `phase-6.sh` → `phase-5.sh` → `phase-4.sh` → `phase-3.sh` → `phase-2.sh` → `phase-1.sh` | PASS (all report ALL PASS) |

Scope boundaries:

- The diff against `main` adds no NuGet package and no `PackageReference`.
- `compose.yml` still defines exactly `auth-api`, `api-a`, and `api-b`; it gained only optional environment entries.
- `grep -rn -i -E "Redis|IDistributedCache|IMemoryCache|AddCors|UseCors" src --include='*.cs'` finds nothing.
- No migration was added (`dotnet ef migrations has-pending-model-changes` reports none).

Focused tests live in `tests/Authentication.IntegrationTests/Scenarios/`: `SecurityConfigurationTests`,
`AccountLockoutTests`, `RateLimitingTests`, `ForwardedHeadersTests`, `BrowserBoundaryTests`,
`AntiEnumerationTests`, and `SecretExposureTests`.

Gate G7 approval by the project owner is **pending** (task T034).
