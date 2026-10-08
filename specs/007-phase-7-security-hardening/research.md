# Research: Phase 7 — Security Hardening

All Technical Context items are resolved; no `NEEDS CLARIFICATION` remains. Each entry records a
decision, its rationale and the alternatives rejected. "Baseline" marks a normative requirement,
"project decision" marks a choice made here within the baseline.

## 1. Identity lockout defaults and overrides

- **Decision**: In the `AddIdentityCore` setup action set `Lockout.MaxFailedAccessAttempts = 5`,
  `Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)` and
  `Lockout.AllowedForNewUsers = true`. Keep the existing
  `Configure<IdentityOptions>(configuration.GetSection("Identity"))`, registered after it, as the
  override path (`Identity__Lockout__MaxFailedAccessAttempts`,
  `Identity__Lockout__DefaultLockoutTimeSpan` as `hh:mm:ss`). Add an options validation that fails
  startup when the count is `< 1` or the span is `<= 0`.
- **Rationale**: Baseline SRS NFR-SEC-BF-002/003/004. Identity's own default span is 5 minutes, so
  the 15-minute value is the actual gap; the count already matches. Configuration binding runs
  after the setup action, so external values win without new keys or code paths.
- **Alternatives**: New `Lockout:*` keys (duplicates the Identity section already used for the
  password policy); setting values only in `appsettings.json` (a missing file or empty override
  would silently fall back to Identity's 5 minutes).

## 2. Request-limit defaults (project decision, FR-019)

The baseline sets no figures. Chosen conservatively for a small internal user base behind one
proxy, per effective client address, fixed window, no queueing:

| Policy | Permits | Window | Reasoning |
|---|---|---|---|
| `login` | 10 | 60 s | Above normal retyping; well below guessing speed; lockout (5) still triggers first for one account. |
| `refresh` | 30 | 60 s | Several tabs and the phase-4 rotation pattern stay far below it. |
| `forgot-password` | 5 | 900 s | A user rarely needs more than one or two per quarter hour. |
| `reset-password` | 10 | 900 s | Allows retyping a rejected new password without allowing token guessing at scale. |
| `forgot-password-address` | 3 | 3600 s | Caps emails toward one mailbox per hour regardless of origin (NFR-SEC-BF-011). |

Operators override each with `RateLimiting:<Policy>:PermitLimit` and `:WindowSeconds`; values must
be positive integers or startup fails. They are reviewed in `docs/phase-7-operations.md`.

- **Alternatives**: Required settings with no default (rejected in clarification); sliding windows
  or token buckets (more parameters to explain, no stated need).

## 3. Rate-limiting mechanism

- **Decision**: `builder.Services.AddRateLimiter` with four named policies built by
  `RateLimitPartition.GetFixedWindowLimiter(partitionKey, ...)`, attached with
  `.RequireRateLimiting(name)` on each endpoint; `app.UseRateLimiter()` after
  `UseExceptionHandler`. `RejectionStatusCode = 429`; `OnRejected` writes ProblemDetails through
  `IProblemDetailsService`, adds `Retry-After` from `MetadataName.RetryAfter` when present and logs
  `RateLimitApplied`.
- **Rationale**: Shared-framework middleware (no package, Constitution IV); per-policy partitions
  give independence (FR-003); rejection happens before the handler (FR-006 edge cases); in-memory
  only (NFR-SEC-BF-010).
- **Alternatives**: A global limiter (cannot keep policies independent); a hand-written counter
  (custom security-relevant code with no framework gap to justify it).

## 4. Deterministic verification of windows (NFR-002 reconciliation)

- **Finding**: In 10.0.12 neither `System.Threading.RateLimiting` nor Identity's `UserManager`
  accepts `TimeProvider`; both read the system clock.
- **Decision**:
  - Lockout expiry: after the fifth failure the test asserts `LockoutEnd` lies within
    `[before + span, after + span]` (bounds around the request, not a duration threshold), then
    moves the persisted `LockoutEnd` into the past through `UserManager.SetLockoutEndDateAsync` and
    proves the correct password authenticates. This is controlled time at the only seam Identity has.
  - Request windows: integration tests and acceptance assert exhaustion, `429` shape,
    independence, partitioning, and that `Retry-After` is present, positive and no longer than the
    configured window, using windows long enough that no step can cross one. Window renewal itself
    is the framework limiter's behavior, verified by inspection of the configured fixed-window
    options (SRS §49.4 method I) plus the reported `Retry-After`; no test or acceptance step sleeps.
- **Rationale**: Satisfies spec NFR-002 and Constitution VI (no real sleeps in time-dependent
  tests) without replacing framework mechanisms with custom clock-aware limiters, which
  Constitution IV/V disfavor.
- **Alternatives**: Custom `RateLimiter` subclass on `TimeProvider` (security code duplicating the
  framework); `Task.Delay` in tests or `sleep` in acceptance (forbidden by Constitution VI).

## 5. Partition key and address normalization

- **Decision**: Key = `HttpContext.Connection.RemoteIpAddress` after forwarded-header processing;
  an IPv4-mapped IPv6 address is converted with `MapToIPv4()`; key string is the canonical
  `ToString()`; a `null` address uses the constant partition `"unknown"`.
- **Rationale**: Spec edge case (one origin for IPv4/IPv6 representations); a missing address must
  still be limited rather than bypass limiting.
- **Alternatives**: Prefix grouping for IPv6 (not required; would need a further project decision).

## 6. Forgot-password address limit

- **Decision**: Api singleton `RecoveryAddressLimiter` owning a `PartitionedRateLimiter<string>`
  (fixed window from `RateLimiting:ForgotPasswordAddress:*`), keyed by
  `ILookupNormalizer.NormalizeEmail(email)` — the same normalizer `FindByEmailAsync` uses. The
  endpoint checks it after body validation (`400` is never consumed) and before readiness and
  lookup, so existing and unknown addresses consume identically. Rejection → the shared `429`
  response and a `RateLimitApplied` event with policy `forgot-password-address` and no address.
  Disposed with the host.
- **Rationale**: SRS NFR-SEC-BF-010/011; body-based keys are not available to middleware
  partitioners before the body is read. In-memory, no external storage.
- **Alternatives**: Middleware partition reading the body (buffering and double parsing);
  limiting only existing accounts (would make `429` an enumeration oracle — FR-007 forbids).

## 7. Order of checks on limited endpoints

`UseRateLimiter` (per origin) → endpoint: body validation `400` → address limit `429`
(forgot only) → origin check `403` (refresh/logout, unchanged position before the cookie) →
readiness `503` → handler. Logout is not limited (not in NFR-SEC-BF-007, spec edge case).

## 8. Forwarded headers and trusted proxies

- **Decision**: `services.Configure<ForwardedHeadersOptions>`: `ForwardedHeaders =
  XForwardedFor | XForwardedProto`; `KnownProxies.Clear()` and `KnownIPNetworks.Clear()` (the
  framework pre-trusts loopback), then add `ReverseProxy:TrustedProxies` (comma-separated IPs) and
  `ReverseProxy:TrustedNetworks` (comma-separated CIDR, parsed with `System.Net.IPNetwork.TryParse`);
  `ForwardLimit = null`; `RequireHeaderSymmetry = false`. `app.UseForwardedHeaders()` is the first
  middleware. Unparsable entries fail startup naming the setting only. `KnownNetworks` (obsolete
  in .NET 10) is not used.
- **Rationale**: SRS NFR-NET-001–003, Technical Constraints §14.4. With the lists empty the
  middleware applies nothing because the connecting address is never trusted (FR-008, scenario 3).
  `ForwardLimit = null` consumes the `X-Forwarded-For` list from the right only while each hop is
  trusted, so entries before the first authorized hop are ignored (spec edge case). Host is not
  forwarded into the app; nothing in the service builds absolute URLs.
- **Alternatives**: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (trusts any source — violates
  NFR-NET-002); trusting the whole Docker default network implicitly (not explicit configuration).

## 9. Reference proxy and acceptance container (FR-010/020)

- **Decision**: `docs/reference-proxy/nginx.conf` — one `server` proxying `/api/auth/` and
  `/health` to `auth-api:8080`; `limit_req_zone $binary_remote_addr` first layer for the four
  sensitive paths (looser than the app: 60 r/min, burst 20, `limit_req_status 429`);
  `proxy_set_header X-Forwarded-For $remote_addr` and `X-Forwarded-Proto $scheme` (overwrite, never
  append the client value); `client_max_body_size 16k`. `tests/acceptance/compose.reference-proxy.yml`
  adds `reference-proxy` (official `nginx` Alpine image, tag and digest pinned when implemented),
  a fixed subnet for the default network, a fixed proxy address, and sets
  `ReverseProxy__TrustedProxies` on `auth-api` to that address only.
- **Rationale**: Clarification Q2; Technical Constraints §37 split (proxy first layer + headers,
  app lockout + limits). App `429` (`application/problem+json`) and Nginx `429` (HTML) are
  distinguishable in acceptance.
- **Alternatives**: Adding Nginx to `compose.yml` (Phase 8; violates FR-020 topology clause).

## 10. Security events (FR-016, NFR-LOG-002, NFR-SEC-ENUM-005)

- **Decision**: `[LoggerMessage]` source-generated events in the existing format
  (`... at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.`, UTC from `TimeProvider`):
  - `LoginFailed` (Warning): `Reason` ∈ `UnknownAccount | WrongPassword | LockedOut | Disabled`,
    `UserId` when known; never the submitted email or password.
  - `AccountLockedOut` (Warning): `UserId`, `LockoutEndUtc`, `Source` ∈ `Login | ChangePassword`;
    emitted when `AccessFailedAsync` leaves the account locked.
  - `RateLimitApplied` (Warning): `Policy`, `ClientAddress` (omitted for the address policy).
- **Rationale**: Events live where the cause is known (Identity adapters, rate-limit boundary);
  `ILogger<T>` only, no new framework. Successful login and other NFR-LOG-002 events not named by
  the spec remain with their owning phases / Phase 8 logging closure.
- **Alternatives**: Returning a cause enum through `IIdentityCredentialValidator` to log in the
  handler (widens an Application contract for a logging concern).

## 11. Unknown-account work equivalence (FR-014)

Already implemented (`IdentityCredentialValidator` verifies against a cached dummy hash; locked
accounts verify too). Verified, not changed: a test substitutes a counting `IPasswordHasher`
decorator in the test host and asserts exactly one `VerifyHashedPassword` for unknown, wrong,
locked and disabled cases — work equivalence measured by calls, not duration (NFR-002).

## 12. Cookie, origin and CORS (FR-011/012)

Verified, not changed: `RefreshCookieWriter` already sets `HttpOnly`, `SameSite=Strict`,
`Path=/api/auth`, `Secure` in Production, and clears with the same attributes;
`BrowserOriginValidator` refuses missing, malformed, duplicated and foreign origins. No
`AddCors`/`UseCors` exists; a test asserts an `OPTIONS` preflight from a foreign origin receives no
`Access-Control-Allow-*` header and that `ICorsService` is not registered. The `Secure` flag is
asserted with the host environment set to `Production`.

## 13. Test address injection

- **Decision**: Test-only `TestConnectionAddressStartupFilter` (registered in
  `AuthenticationApiFactory.ConfigureTestServices`) runs before every other middleware and sets
  `Connection.RemoteIpAddress` from the `X-Test-Connection-Address` request header (default
  `192.0.2.10`). It models the TCP peer, so `UseForwardedHeaders` then behaves exactly as in
  production. The factory applies lifted limits (`10000` permits) unless a test passes its own
  `RateLimiting__*` settings, keeping Phase 1–6 regression unaffected.
- **Alternatives**: Kestrel-hosted tests on multiple loopback addresses (platform dependent).

## 14. Regression of acceptance scripts

Phase 1–6 scripts issue at most 14 refresh and 6 forgot requests per run from one host: the
forgot count (6) exceeds the 5-per-15-minutes default, and repeated runs would too. `phase-7.sh`
and earlier scripts export generous `AUTH_RATE_LIMIT_*` values for regression runs, while
`phase-7.sh` uses defaults or small explicit values in its own limit checks.
