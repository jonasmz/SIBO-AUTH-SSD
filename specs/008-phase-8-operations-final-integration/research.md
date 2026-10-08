# Research: Phase 8 — Operations, Deployment Integration and Final Acceptance

All Technical Context items are resolved; no `NEEDS CLARIFICATION` remains. "Baseline" marks a
normative requirement; "project decision" marks a choice made here within the baseline.

## 1. Persistent file logging (TC §15.3–15.6)

- **Decision**: A first-party `ILoggerProvider` in `Authentication.Infrastructure/Logging`,
  `[ProviderAlias("File")]`. `Log` checks `IsEnabled`, formats the line on the calling thread
  (so `Activity.Current` trace/span are captured where they exist) and writes it to an unbounded
  `Channel<string>` (`SingleReader = true`). One long-running background task owned by the
  provider appends lines to the current file through a `StreamWriter` (UTF-8 without BOM,
  `AutoFlush` after each drained batch). `Dispose` completes the channel and waits for the writer
  to drain.
- **Rationale**: Baseline TC §15.4 asks for an in-memory queue plus a background writer and no
  significant blocking I/O in `ILogger.Log`; a single reader makes lines atomic and ordered, so
  concurrent writers can never interleave (spec edge case) and nothing is dropped (unbounded).
- **Alternatives**: Bounded channel with drop policy (loses lines, contradicts "never lose");
  locking a `FileStream` inside `Log` (blocking I/O on the request path); Serilog/NLog (prohibited,
  TC §34).

## 2. Line format, file naming, rotation, retention (project decision within TC §15.4–15.5)

- **Line**: `{utc:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level}] {Category}[{EventId}] trace={TraceId} span={SpanId} {Message}`;
  the `[EventId]`, `trace=`, `span=` parts are omitted when absent; an exception appends
  ` exception={Type}` and its stack trace on the same line with newlines escaped as `\n`. The
  exception message is not written (it may echo input). Message newlines are escaped so every event
  is exactly one line.
- **File**: `auth-yyyy-MM-dd.log` by the UTC date of the event (TC §15.5 naming). The writer
  switches file when an event's UTC date differs from the open file's date.
- **Retention**: `Logging:File:RetentionDays` (default 30, integer ≥ 1). At startup and on every
  rotation the writer deletes `auth-*.log` whose date in the name is older than
  `today - retention`; files not matching the pattern are never touched. Shortening the retention
  takes effect at the next startup or rotation (spec edge case).
- **Time**: `TimeProvider` from DI (the provider is registered as a DI singleton), so rotation and
  retention are unit-tested with a controlled clock and no sleeps.

## 3. Log directory configuration and fail-fast

- **Decision**: `Logging:File:Directory` is required. `AddAuthenticationInfrastructure` validates it
  with the existing `DataProtectionStorageOptions.IsUsableDirectory` probe (exists and writable)
  and throws naming only the setting, like `DataProtection:KeysPath`. Compose sets it to `/app/logs`
  (TC §15.6) and bind-mounts `${AUTH_LOGS_HOST_PATH:?}` there.
- **Rationale**: Spec assumption and edge case (missing/unwritable → startup failure naming the
  setting); consistent with the other required storage paths.
- **Alternatives**: Creating the directory automatically (hides a wrong mount); optional file
  logging (SRS NFR-LOG requires persistent logs in the deployment).

## 4. Console logging

- **Decision**: `builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.UseUtcTimestamp = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ "; })`.
  Events already carry trace/span in their message, so both sinks show the same correlation.
- **Rationale**: Official console provider (TC §15.2), one line per event suits `docker logs`.
- **Alternatives**: JSON console formatter (valid, but the file format is line text; one style for
  both keeps review simple).

## 5. Missing security events (SRS NFR-LOG-002, Roadmap §14.2)

Present (reused): `LoginFailed`, `AccountLockedOut`, `RateLimitApplied`, logout family revocation,
password changed, `PasswordReset`, refresh replay detected, sessions revoked. Missing and added to
`Identity/SecurityEvents` (same message style, UTC from `TimeProvider`, trace/span):

| Event | Emitted by | Fields |
|---|---|---|
| `LoginSucceeded` | `IdentityCredentialValidator` after reset of the failed count | user id |
| `UserCreated` | `UserAdministration.CreateAsync` after commit | user id, initial role count |
| `UserEnabled` / `UserDisabled` | `UserAdministration.SetEnabledAsync` when the state actually changes | user id |
| `UserRoleAssigned` / `UserRoleRemoved` | `UserAdministration.ReplaceRolesAsync`, one per role in the added/removed diff, after commit | user id, role name |

No email, password, token, or hash. Role rename/delete keep their existing behavior; they are not
in the NFR-LOG-002 list and gain no event.

## 6. OpenAPI generation (TC §12, SRS NFR-DOC-001/002)

- **Decision**: `Microsoft.AspNetCore.OpenApi` 10.0.12, document `v1`, OpenAPI 3.1 (framework
  default in .NET 10). A document transformer registers the `Bearer` HTTP scheme (`bearer`, `JWT`);
  an operation transformer adds the security requirement to operations whose endpoint metadata
  contains `IAuthorizeData` and not `IAllowAnonymous`. Each endpoint declares `WithSummary`, its
  success type with `Produces<T>(status)` (or `Produces(204)`), and `ProducesProblem(status)` for
  every error it already returns; coverage per [contracts/openapi-coverage.md](contracts/openapi-coverage.md).
  Request bodies are the existing record types read with `ReadFromJsonAsync`. Implementation finding:
  `Accepts<T>("application/json")` adds content-type metadata that the framework enforces with `415`
  before the endpoint runs, replacing the endpoints' own `400` problem details for non-JSON bodies (four
  Phase 3–6 tests caught it). Endpoints therefore declare their body with a documentation-only marker
  (`DocumentsJsonRequest<T>()`) that an operation transformer turns into the `requestBody`; routing is
  unaffected. A schema transformer drops the records' computed get-only helpers (`IsValid`,
  `TrimmedName`) from request schemas without changing the records.
- **Rationale**: Official mechanism; metadata only, so behavior is unchanged (FR-006).
- **Alternatives**: Swashbuckle/NSwag (prohibited); XML comments only (do not express status codes).

## 7. Read-only Scalar and environment exposure (TC §13, SRS NFR-DOC-003/004)

- **Decision**: `Scalar.AspNetCore` 2.17.9, mapped with `MapScalarApiReference` only when
  `IsDevelopment()`, alongside `MapOpenApi()`. Options: `HideTestRequestButton = true`,
  `HideClientButton = true`, `PersistentAuthentication = false`, telemetry disabled, no
  authentication preset. In Production neither route exists (404 from auth-api) and Nginx routes
  `/auth/*` into `/api/auth/*` and `/auth/admin/*` into `/api/admin/*`, so `/auth/openapi/...` and
  `/auth/scalar` resolve to routes that do not exist; documentation is unreachable from outside twice
  over. The
  authorized-network exception is not implemented (spec: permitted, not required).
- **Version**: 2.17.9 published 2026-09-24 (two weeks before planning), declares no package
  dependencies; 2.17.13 is newer than the project's two-week rule. `Microsoft.OpenApi`, pulled
  transitively by `Microsoft.AspNetCore.OpenApi`, is pinned centrally at the version the restore
  resolves (central transitive pinning is enabled).

## 8. Frontend service and routing (SRS §4.2/4.3, NFR-DEPLOY-001/002, TC §14, §37)

- **Decision**: Service `frontend` uses the official `nginx` stable Alpine image pinned by the
  digest validated in Phase 7, with three read-only bind mounts: `deploy/frontend/nginx.conf`,
  the operator's compiled static files (`${FRONTEND_STATIC_HOST_PATH:?}` → `/usr/share/nginx/html`),
  and the TLS certificate/key directory (`${FRONTEND_TLS_HOST_PATH:?}` → `/etc/nginx/tls`). No custom
  image is built. Routes per [contracts/deployment-topology.md](contracts/deployment-topology.md):
  the public Authentication API contract (spec FR-010a) is `/auth/<operation>` → `/api/auth/<operation>`
  and `/auth/admin/<resource>` → `/api/admin/<resource>` (an `^~ /auth/admin/` location first, then
  a regex location for the four limited operations, then `/auth/`); `/api-a/api/*` and `/api-b/api/*`
  keep their prefixes and strip them; any other `/api-a/...`, `/api-b/...` is `404`; `/` serves the
  static files with SPA fallback to `index.html`. Redundant or internal forms (`/auth/api/auth/login`,
  `/auth/health/...`) translate into `/api/auth/...` paths that have no route and return `404`, so no
  extra deny rules are needed.
- **Refresh cookie**: auth-api keeps `Path=/api/auth`; Nginx `proxy_cookie_path /api/auth /auth;`
  rewrites only the path on issue, rotation, and clearing, so the browser stores the cookie at
  `Path=/auth` and returns it to `/auth/refresh` and `/auth/logout` (FR-011). Its other attributes are
  untouched. It also accompanies `/auth/admin/*` requests, which ignore it. The Origin check is
  unchanged: `Security:FrontendOrigin` is the external origin.
- **Location headers**: created users and roles return `Location: /api/admin/...`; `proxy_redirect
  /api/admin/ /auth/admin/;` rewrites them so no internal URL reaches the browser (FR-010a).
- **First limiting layer**: the Phase 7 reference rules (zone per `$binary_remote_addr`, 60 r/min,
  burst 20, `429`) on the four sensitive routes; `X-Forwarded-For $remote_addr` and
  `X-Forwarded-Proto $scheme` overwritten; `client_max_body_size 16k`.
- **TLS**: Nginx listens on 443 with the mounted `tls.crt`/`tls.key` and redirects 80 → 443
  (Constitution VII: HTTPS terminated at the proxy). Acceptance generates a disposable self-signed
  certificate.
- **Alternatives**: A custom frontend image (would embed product files; the clarification makes
  them operator input); public `/auth/api/...` paths mirroring the internal ones (redundant, rejected
  by the clarification); changing auth-api routes or adding an app-side path base (changes the
  Phase 1–7 contracts, FR-010a/FR-023); a configurable cookie path in the app (new setting where the
  proxy already solves it).

## 9. Internal network and proxy trust (FR-012, FR-014)

- **Decision**: `compose.yml` declares the default network with an explicit subnet
  `${AUTH_INTERNAL_SUBNET:-172.30.80.0/24}`; `frontend` gets `ipv4_address`
  `${FRONTEND_INTERNAL_ADDRESS:-172.30.80.10}`; `auth-api` sets `ReverseProxy__TrustedProxies` to
  `${AUTH_TRUSTED_PROXIES:-<frontend address>}`. Backends publish no port; only `frontend`
  publishes `${FRONTEND_HTTPS_PORT:-443}` and `${FRONTEND_HTTP_PORT:-80}`.
- **Rationale**: Phase 7 requires an explicit trusted address; a fixed address is explicit and
  narrower than trusting the whole network (which would include api-a/api-b).
- **Phase 7 override**: `tests/acceptance/compose.reference-proxy.yml` currently redefines the
  default network's `ipam`. It is changed to rely on `compose.yml`'s variables (the Phase 7 script
  exports `AUTH_INTERNAL_SUBNET=172.28.7.0/24`), so two `ipam` definitions never merge; its
  assertions are unchanged.

## 10. Direct backend access for acceptance only

- **Decision**: `tests/acceptance/compose.direct-access.yml` re-publishes `auth-api`, `api-a`,
  `api-b` ports (`${AUTH_HTTP_PORT}`, `${API_A_HTTP_PORT}`, `${API_B_HTTP_PORT}`) for the Phase 1–7
  procedures, which address backends directly. `compose.yml` itself never publishes them.
- **Harness adaptation**: a shared `tests/acceptance/deployment-env.sh` (sourced by every phase
  script) exports `COMPOSE_FILE` with the direct-access override appended, and creates disposable
  `AUTH_LOGS_HOST_PATH`, `FRONTEND_STATIC_HOST_PATH` (test page) and `FRONTEND_TLS_HOST_PATH`
  (self-signed pair). Compose validates `${VAR:?}` across the whole file even when a script starts
  only `auth-api`, so these must exist for every script. Phase 1–7 assertions stay unchanged
  (spec US4/AC6); only their environment setup changes, as Phase 7 already did for rate limits.
- **Alternatives**: `compose.override.yml` (auto-loaded, would publish backend ports on a plain
  production `docker compose up`); keeping ports in `compose.yml` (violates FR-012).

## 11. SQLite backup and restore (SRS NFR-BACKUP-001–006, TC §39)

- **Decision**: Backup with the SQLite online backup API from the host:
  `sqlite3 "$AUTH_SQLITE_HOST_PATH/auth.db" ".backup '$TARGET'"` followed by
  `sqlite3 "$TARGET" 'PRAGMA integrity_check;'` (must print `ok`). Runs while auth-api is writing;
  needs read access to the data directory (operator). Restore: `docker compose stop auth-api`,
  move the current `auth.db` (and any `auth.db-journal`/`-wal`/`-shm`) aside, copy the backup in with
  the container user's ownership and mode, `docker compose start auth-api`, wait for
  `/health/ready` (startup applies pending migrations for older backups and never recreates a
  current database). Keys and key ring are static files backed up by plain copy; documented.
- **Evidence**: acceptance takes the backup while a loop of logins and refreshes runs, then starts
  a separate disposable Compose project on a fresh data directory holding only the restored file
  and verifies users, roles, persisted session rows, and a known login (FR-021).
- **Alternatives**: `cp` of a live file (inconsistent); `VACUUM INTO` (also consistent, but drops
  the backup-API page semantics operators know; acceptable fallback, documented); a backup
  container or scheduler (prohibited).

## 12. End-to-end acceptance (Roadmap §14.8, spec US4)

- **Decision**: `tests/acceptance/phase-8.sh` against `compose.yml` + the Phase 6 mail sink
  override only (no direct-access override), all traffic through `https://localhost:$FRONTEND_HTTPS_PORT`
  with `curl --cacert` on the disposable certificate and the frontend origin as `Origin`. Order:
  empty storage → admin login → password change → user + role → user login → API A/B → refresh
  rotation and replay refusal → logout → re-login → forgot (mail sink) → reset → old sessions
  revoked → disable → login refused → lockout → lockout recovery → application and proxy `429` →
  backend ports unreachable from the host and private key absent from consumers → log file
  present with events and no secrets → restart / `up --build` / `--force-recreate` / `down -v` +
  `up` state checks → backup during writes and restore in a disposable project → Phase 7
  acceptance (chains 6→1).
- **Lockout recovery**: The script moves the persisted `LockoutEnd` of the disposable database
  into the past with `sqlite3` and then signs in, like the Phase 7 integration tests; it never
  waits out the 15 minutes (Constitution VI, NFR-002).
- **Readiness**: polls `/health/ready` from inside the network via `docker compose exec frontend
  wget -qO- http://auth-api:8080/health/ready` (health is not routed externally).

## 13. Production environment and images

- `compose.yml` sets `ASPNETCORE_ENVIRONMENT=Production` explicitly for the three .NET services
  (cookie `Secure`, no documentation endpoints). Images keep the official `10.0` tags in
  development; the operations document records the exact digests validated by the Gate G8 run
  (TC §28.4).
- auth-api mounts: data, key ring, keys (read-only), logs. Consumers mount only the public key file.
