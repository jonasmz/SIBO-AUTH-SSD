# Quickstart: Phase 8 Validation

## Prerequisites

.NET 10 SDK (`global.json`), Docker with Compose v2, `curl`, `jq`, `openssl`, `sqlite3` (≥ 3.46.1)
on the host.

## Automated suite

```bash
dotnet build Authentication.slnx --no-incremental -warnaserror
dotnet test
```

| Scenario | Proves |
|---|---|
| `PersistentFileLoggerProviderTests` (unit) | one line per event with UTC, level, category, event id, trace/span; escaped newlines; exception type without message; UTC daily rotation and retention with a controlled clock; many concurrent writers → no interleaving or loss; dispose drains the queue |
| `OperationalEventsTests` (integration) | the six new events appear for login success, user creation, enable/disable, role assign/remove; the host writes them (and the existing ones) to `auth-<utc-date>.log` in the configured directory; no secrets in that file; a missing or unwritable `Logging:File:Directory` or invalid retention stops startup naming only the setting |
| `ApiDocumentationTests` (integration) | Development: `/openapi/v1.json` is OpenAPI 3.1 and matches [contracts/openapi-coverage.md](contracts/openapi-coverage.md) (paths, methods, statuses, Bearer requirements); `/scalar` is served read-only. Production: both return 404 |
| `OlderBackupRestoreTests` (integration) | a database migrated only to an earlier migration, standing in for an older backup, is migrated forward at startup without being recreated; its user and the administrator's password survive and readiness is reported |

No test waits for wall-clock time. Phase 1–7 tests run unchanged.

## Acceptance (disposable, production-equivalent)

```bash
tests/acceptance/phase-8.sh
```

Uses `compose.yml` plus only the Phase 6 mail-sink override; everything goes through
`https://localhost:$FRONTEND_HTTPS_PORT` with a disposable self-signed certificate. Expected `PASS`
lines, in order:

1. Empty storage → exactly four services; schema and administrator created; `/health/ready` OK
   (checked from inside the network).
2. Static test page served at `/`. URL translation: each public URL of the contract table in
   [contracts/deployment-topology.md](contracts/deployment-topology.md) reaches its internal route
   (it answers with that route's own status and body shape, e.g. `401` problem details for a bad login,
   never the static page or a proxy `404`), a created user's `Location` starts with `/auth/admin/users/`, and
   `/auth/api/auth/login`, `/auth/health/ready`, `/auth/openapi/v1.json`, `/auth/scalar`, and
   `/api-a/health/live` → 404.
3. Backend ports not reachable from the host; consumers' mounts hold no private key; the private key
   is owned by the container user with mode `0600` in a `0700` directory and `auth-api` signs with it.
4. E2E through the public URLs only (`/auth/*`, `/auth/admin/*`, `/api-a/api/*`, `/api-b/api/*`),
   with a cookie jar acting as the browser: admin login and password change; user + role; user login
   whose `Set-Cookie` is `auth_refresh` with `Path=/auth`, `HttpOnly`, `Secure`, `SameSite=Strict`;
   API A and API B accept the token; `/auth/refresh` sends the jar's cookie, rotates it (new value,
   same public path and attributes); replay of the old value refused; `/auth/logout` clears it with
   `Path=/auth` and the cleared jar can no longer refresh; refresh from a foreign `Origin` refused;
   re-login; forgot (mail sink) and reset; previous sessions revoked; disable → login refused;
   lockout, recovery after moving the persisted lockout end; application `429` and proxy `429`; a
   client-forged `X-Forwarded-For` through the entry point changes nothing and `RateLimitApplied` names
   the client's address, never the frontend's internal address.
5. Log file `auth-<utc-date>.log` exists on the host with every NFR-LOG-002 event, UTC time and
   trace; no secret in console or file.
6. `restart`, `up --build`, `up --force-recreate`, then `down -v` + `up -d`: database, key ring,
   private key, logs still present; users, roles, changed admin password, sessions, and API A/B
   validation intact.
7. Backup during concurrent logins/refreshes → `integrity_check` ok → restore into a separate
   disposable project → users, roles, sessions present and a known account signs in.
8. Phase 7 acceptance (chains 6 → 1) passes.

Teardown removes every disposable path and both Compose projects.

## Gate G8

Record evidence and the validated image digests in `docs/phase-8-operations.md` against Roadmap §14.9.
