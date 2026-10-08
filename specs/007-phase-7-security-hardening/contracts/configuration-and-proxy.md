# Contract: Phase 7 Configuration and Proxy Boundary

## Settings (all optional; defaults apply when unset or blank)

| Key (env form) | Default | Rule |
|---|---|---|
| `Identity__Lockout__MaxFailedAccessAttempts` | `5` | int ≥ 1 |
| `Identity__Lockout__DefaultLockoutTimeSpan` | `00:15:00` | TimeSpan > 0 |
| `RateLimiting__Login__PermitLimit` / `__WindowSeconds` | `10` / `60` | positive ints |
| `RateLimiting__Refresh__PermitLimit` / `__WindowSeconds` | `30` / `60` | positive ints |
| `RateLimiting__ForgotPassword__PermitLimit` / `__WindowSeconds` | `5` / `900` | positive ints |
| `RateLimiting__ResetPassword__PermitLimit` / `__WindowSeconds` | `10` / `900` | positive ints |
| `RateLimiting__ForgotPasswordAddress__PermitLimit` / `__WindowSeconds` | `3` / `3600` | positive ints |
| `ReverseProxy__TrustedProxies` | empty | comma-separated IP addresses |
| `ReverseProxy__TrustedNetworks` | empty | comma-separated CIDR networks |

Any invalid value terminates startup with a message naming only the setting. Defaults are a
project decision ([research.md §2](../research.md)).

## Compose variables (`compose.yml`, `auth-api` only; all optional)

| Variable | Maps to | Compose default |
|---|---|---|
| `AUTH_LOCKOUT_MAX_FAILED_ATTEMPTS` | `Identity__Lockout__MaxFailedAccessAttempts` | `5` |
| `AUTH_LOCKOUT_DURATION` (`hh:mm:ss`) | `Identity__Lockout__DefaultLockoutTimeSpan` | `00:15:00` |
| `AUTH_RATE_LIMIT_LOGIN_PERMITS` / `_WINDOW_SECONDS` | `RateLimiting__Login__PermitLimit` / `__WindowSeconds` | blank → default |
| `AUTH_RATE_LIMIT_REFRESH_PERMITS` / `_WINDOW_SECONDS` | `RateLimiting__Refresh__*` | blank → default |
| `AUTH_RATE_LIMIT_FORGOT_PERMITS` / `_WINDOW_SECONDS` | `RateLimiting__ForgotPassword__*` | blank → default |
| `AUTH_RATE_LIMIT_RESET_PERMITS` / `_WINDOW_SECONDS` | `RateLimiting__ResetPassword__*` | blank → default |
| `AUTH_RATE_LIMIT_FORGOT_ADDRESS_PERMITS` / `_WINDOW_SECONDS` | `RateLimiting__ForgotPasswordAddress__*` | blank → default |
| `AUTH_TRUSTED_PROXIES` | `ReverseProxy__TrustedProxies` | blank → none |
| `AUTH_TRUSTED_NETWORKS` | `ReverseProxy__TrustedNetworks` | blank → none |

Lockout entries carry non-empty defaults because Identity's configuration binding does not treat a
blank value as "unset"; the application's own `RateLimiting`/`ReverseProxy` parsing does.

## Application trust contract

- Honored headers: `X-Forwarded-For`, `X-Forwarded-Proto` — only when the connecting peer (and each
  further hop) is in the trusted set. `X-Forwarded-Host` and `Forwarded` are never honored.
- With an empty trusted set, no forwarded header has any effect.
- The resulting address is the only identity used by per-origin limits and `RateLimitApplied`.

## Obligations of the designated reverse proxy

1. **Overwrite** `X-Forwarded-For` with the peer address (`$remote_addr`); never append a
   client-supplied value.
2. Set `X-Forwarded-Proto` to the scheme the client used; forward `Host`.
3. Apply a first limiting layer to `/api/auth/login`, `/refresh`, `/forgot-password`,
   `/reset-password`, looser than the application's, returning `429`.
4. Limit request body size.
5. Be the only route to `auth-api` in production; its address (or network) is the trusted set.

The proxy layer never replaces Identity lockout or application limits (Technical Constraints §37).
Reference implementation: `docs/reference-proxy/nginx.conf`; exercised only by
`tests/acceptance/compose.reference-proxy.yml`.
