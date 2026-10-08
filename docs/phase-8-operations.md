# Phase 8 Operations

Phase 8 integrates the system into one production topology: `frontend` (Nginx, the only external entry
point), `auth-api`, `api-a` and `api-b`. This guide covers persistence and recovery first. The deployment
procedure, public URLs, logging, OpenAPI exposure, acceptance instructions and the Gate G8 evidence follow.
The Angular source and its build belong to the frontend project; this repository only serves its compiled
files. Gate G8 approval and the closing commit or tag are controlled by the project owner.

## Persistent paths

All critical state lives on **host bind mounts**, outside the Compose lifecycle. None is a Compose-managed
volume, so `docker compose down`, `down -v`, `up --build` and `up --force-recreate` never remove it.
Deleting data requires removing the host path explicitly.

| What | Host variable | Container path | Owner / mode (reference) |
|---|---|---|---|
| SQLite database (`auth.db`) | `AUTH_SQLITE_HOST_PATH` | `/var/lib/auth-api/data` | container UID (`APP_UID`, 1654), directory `0700` |
| Data Protection key ring (reset tokens) | `AUTH_DATAPROTECTION_HOST_PATH` | `/var/lib/auth-api/dataprotection` | container UID, directory `0700` |
| JWT private key (`jwt-private.pem`) | `AUTH_RSA_HOST_PATH` | `/var/lib/auth-api/keys` (read-only, `auth-api` only) | container UID, file `0600`, directory `0700` |
| JWT public key | `AUTH_JWT_PUBLIC_KEY_HOST_FILE` | `/var/lib/consumer/jwt-public.pem` (read-only, `api-a`/`api-b`) | `0644` |
| Daily log files (`auth-yyyy-MM-dd.log`) | `AUTH_LOGS_HOST_PATH` | `/app/logs` | container UID, directory `0750` |
| Compiled frontend files | `FRONTEND_STATIC_HOST_PATH` | `/usr/share/nginx/html` (read-only) | readable by Nginx |
| TLS pair (`tls.crt`, `tls.key`) | `FRONTEND_TLS_HOST_PATH` | `/etc/nginx/tls` (read-only) | key `0600`, readable by Nginx |

The private key and the key ring are plain files: copying them (preserving ownership and mode) is a
complete backup of each. Losing the private key invalidates every issued access token and forces a new
public key to be distributed to the consumers; losing the key ring invalidates outstanding reset tokens.

Operational note: Nginx resolves `auth-api`, `api-a` and `api-b` when it starts. After recreating a backend
on its own, restart or recreate `frontend` as well (`docker compose up -d --force-recreate` does both).

## Lifecycle guarantees

The acceptance script `tests/acceptance/phase-8.sh` establishes users, a role and its assignment, a changed
administrator password and an active refresh session, then runs `docker compose restart`,
`up --build`, `up --force-recreate`, and `down -v` followed by `up -d` on the same host paths. After each it
verifies that the database, key ring, private key (ownership and mode included) and log files exist; that the
users, role, changed password and refresh session are intact; and that API A and API B validate tokens signed
with the persisted key. It uses only disposable paths created for the run.

## Backup

Back up the database while the service runs with SQLite's online-backup command, never by copying the live
file:

```sh
sqlite3 "$AUTH_SQLITE_HOST_PATH/auth.db" ".timeout 10000" ".backup '/path/to/backup/auth.db'"
sqlite3 /path/to/backup/auth.db "PRAGMA integrity_check;"      # must print: ok
```

- `.backup` produces a consistent copy even while logins and refreshes are writing; the integrity check is
  required, and a copy that merely exists is not a backup.
- Run `sqlite3` as the container user (or any user able to read the directory); the acceptance script runs it
  in a throwaway container so no host installation is needed.
- Also copy the JWT private key and the key ring (plain files) with their ownership and modes, and keep the
  backup with the same protection as the originals: it contains password hashes and session digests.

## Restore

1. Stop `auth-api` (`docker compose stop auth-api`; stop `frontend` too if it should not serve meanwhile).
2. Move the current database **and** its `auth.db-journal`, `auth.db-wal` and `auth.db-shm` side files, if any,
   out of `AUTH_SQLITE_HOST_PATH`; do not delete them until the restore is verified.
3. Copy the backup in as `auth.db`, owned by the container user with mode `0600`
   (`chown 1654:1654` / `chmod 0600`).
4. Start (`docker compose up -d`) and wait for `GET /health/ready` (inside the network) to answer `200`.
5. Verify a known login, the users and roles, and, if sessions matter, that a refresh cookie issued before the
   backup still renews.

A backup from an older version is accepted: on start `auth-api` applies any pending migration to the restored
file, keeps the existing users (including the administrator's password) and does not recreate the database
(covered by `OlderBackupRestoreTests`). A newer backup than the running image is not supported.

The acceptance script proves the procedure by restoring into a **separate** disposable Compose project (its own
project name, network, frontend address and ports, so it never collides with the running one) on a fresh data
directory holding only the restored file.

## Deployment procedure

Inputs (see `.env.example`, Phase 8 section): `AUTH_SQLITE_HOST_PATH`, `AUTH_DATAPROTECTION_HOST_PATH`,
`AUTH_RSA_HOST_PATH` (with `jwt-private.pem`), `AUTH_JWT_PUBLIC_KEY_HOST_FILE`, `AUTH_LOGS_HOST_PATH`,
`FRONTEND_STATIC_HOST_PATH`, `FRONTEND_TLS_HOST_PATH` (`tls.crt`, `tls.key`), plus the JWT, SMTP and
`AUTH_FRONTEND_ORIGIN` settings of earlier phases (`AUTH_FRONTEND_ORIGIN` must be the external `https://` origin).

1. Create the host paths with the owners and modes of the persistence table above.
2. Copy the compiled frontend application into `FRONTEND_STATIC_HOST_PATH` (built by the frontend project).
3. Place the TLS pair in `FRONTEND_TLS_HOST_PATH`.
4. `docker compose up -d`. `auth-api` applies migrations and creates the initial administrator itself; no
   migration or bootstrap step exists. Change the initial administrator password after the first login.

Compose defines exactly four services (`frontend`, `auth-api`, `api-a`, `api-b`). Only the frontend publishes
ports. Image digests: the frontend uses `nginx:stable-alpine@sha256:0985e772fb9f729e6fa0980da05fca5d9c468e870eed43071545afa9d2e27d94`;
the .NET runtime image validated by the run below is
`mcr.microsoft.com/dotnet/aspnet@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4`.

## Public URLs

| Public URL | Internal route |
|---|---|
| `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/change-password`, `/auth/forgot-password`, `/auth/reset-password` | the same operation under `/api/auth/` |
| `/auth/admin/users…`, `/auth/admin/roles…` | the same path under `/api/admin/` |
| `/api-a/api/…`, `/api-b/api/…` | `api-a` / `api-b` `/api/…` |
| `/` and any other path | static files with single-page-application fallback |

Health, OpenAPI and Scalar are never reachable through the entry point (they answer `404`). The refresh cookie
is stored by the browser with `Path=/auth`. Plain HTTP is redirected to HTTPS.

## Logging

- **Settings:** `Logging:File:Directory` (required, must exist and be writable; Compose sets `/app/logs`) and
  `Logging:File:RetentionDays` (default `30`, integer ≥ 1; Compose maps `AUTH_LOG_RETENTION_DAYS`). An invalid
  value stops startup naming only the setting.
- **Files:** `auth-yyyy-MM-dd.log` by the UTC date, one event per line, UTF-8; files named like that and older
  than the retention are deleted at startup and at each rotation, other files are never touched.
- **Line:** `<UTC time> [Level] Category[EventId] trace=<id> span=<id> <message>`; newlines in messages are
  escaped; an exception is written as its type and stack trace, never its message. The console shows the same
  events on one line with UTC timestamps.
- **Events:** login success and failure, account lockout, rate limit applied, logout, password change, password
  reset, user created, enabled, disabled, role assigned and removed, refresh credential reuse detected, and
  session revocation.
- **Secret-free:** no password, access or refresh token, reset token, private key or SMTP credential is logged;
  the acceptance run checks both the console output and the file for every secret it used.

## OpenAPI

`GET /openapi/v1.json` (OpenAPI 3.1) and the read-only Scalar viewer at `/scalar` exist **only in Development**.
In Production both are unrouted (`404`), and through the frontend they are unreachable in any environment. The
document uses the `Bearer` HTTP scheme; the viewer hides the test-request and client buttons and keeps no
credentials.

## Acceptance

`tests/acceptance/phase-8.sh` (requires Docker Compose, `openssl`, `curl`, `jq`; disposable paths only) runs the
deployment, lifecycle, backup/restore and end-to-end sections and the log review, then the Phase 7 acceptance
(which chains Phases 6 to 1). Reusable validation guide:
`specs/008-phase-8-operations-final-integration/quickstart.md`.

## Gate G8 evidence

### Scope boundaries (T032)

| Check | Result |
|---|---|
| `docker compose config --services` | `api-a api-b auth-api frontend` (exactly four) — PASS |
| Package diff against `main` | adds only `Microsoft.AspNetCore.OpenApi` 10.0.12, `Scalar.AspNetCore` 2.17.9 and the pinned transitive `Microsoft.OpenApi` 2.12.0 — PASS |
| Migrations / model | no migration file added; `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration" (Design package added temporarily, reverted) — PASS |
| Extra infrastructure | no logging framework, collector, queue, cache or gateway in `Directory.Packages.props` or `compose.yml` — PASS |

### Five evidence states (T034, T035)

| State | Command | Result |
|---|---|---|
| Build | `dotnet build Authentication.slnx --no-incremental -warnaserror` | 0 errors, 0 warnings — PASS |
| Tests | `dotnet test` | 202 of 202 passed (unit and integration) — PASS |
| Startup | `tests/acceptance/phase-8.sh`, deployment section | four services from empty storage, ready — PASS |
| Feature | same script: lifecycle, backup/restore, end-to-end, log review | all PASS (below) |
| Regression | same script, chained `phase-7.sh` (Phases 6 to 1) | `Phase 7 acceptance: ALL PASS` |

Acceptance output of the run (images validated: the digests above):

```text
PASS  private key owned by the container user (uid 1654), file 0600 in a 0700 directory
PASS  compose.yml alone defines exactly four services; from empty storage auth-api migrated, bootstrapped and reports ready
PASS  the entry point serves the application files over HTTPS and redirects plain HTTP
PASS  every public URL reaches its unchanged internal route; created users are located under /auth/admin/
PASS  redundant (/auth/api/auth/login) and internal (health, OpenAPI, Scalar) routes answer 404 through the entry point
PASS  no backend publishes a port; auth-api signs with its restricted private key and the consumers hold only the public key
PASS  baseline: database, key ring, private key, logs, users, role, changed password, session and token validation intact
PASS  docker compose restart: database, key ring, private key, logs, users, role, changed password, session and token validation intact
PASS  docker compose up --build: database, key ring, private key, logs, users, role, changed password, session and token validation intact
PASS  docker compose up --force-recreate: database, key ring, private key, logs, users, role, changed password, session and token validation intact
PASS  docker compose down -v + up: database, key ring, private key, logs, users, role, changed password, session and token validation intact
PASS  a SQLite backup taken while the service was writing passes the integrity check (2 users, 9 session families)
PASS  the backup restored into a separate disposable project: integrity ok, known login, user, role and persisted session intact
PASS  login sets auth_refresh with Path=/auth, HttpOnly, Secure and SameSite=Strict; API A and API B accept the token
PASS  refresh rotates the cookie under /auth; a foreign Origin is refused; replaying the old value is refused and revokes the session
PASS  logout clears the cookie under Path=/auth and the cleared jar can no longer renew
PASS  forgot-password delivers a token through the mail sink; the reset revokes the earlier sessions and replaces the password
PASS  a disabled user is rejected at login; re-enabling and role removal take effect
PASS  five wrong passwords lock the account; with the lockout end moved into the past the correct password works again
PASS  the application answers 429 problem+json when a limit is exceeded
PASS  forged X-Forwarded-For requests are limited as one client; the proxy's own HTML 429 is distinct from the application's problem+json 429
PASS  the daily log holds every required event with UTC time and trace identifier, the client origin as the frontend saw it, and no secret (console included)
PASS  five wrong passwords lock the account: even the correct password is refused with the generic 401
PASS  login 429 (problem+json, Retry-After) while the other policies still answer; the lockout stayed in force
PASS  refresh and forgot-password limits (per origin and per address) answer 429 independently
PASS  reset-password limit reached through the reference proxy (application 429)
PASS  the reference proxy applies its own first-layer limit (HTML 429), distinct from the application's
PASS  the effective origin is the real peer directly and the original client through the proxy; the forged value never appears
PASS  auth-api logs hold the lockout, login-failure, and rate-limit events with UTC time and trace, and no secret or email
PASS  forgot-password answers the identical 204 for an existing and an unknown address; invalid input is 400
PASS  one email was delivered over SMTP to the existing account only, carrying the reset token
PASS  restart: auth-api ready again
PASS  up --force-recreate: container recreated
PASS  down -v + up on the same host directories: stack back
PASS  the key ring is a host bind mount (no Compose volume), owned by the container user, mode 0700, holding persisted keys
PASS  the still-valid token reset the password after restart, recreation, and down -v
PASS  reset: old password rejected, new accepted, every session revoked, token single-use
PASS  auth-api logs hold no token, password, SMTP secret, or cookie, and record the recovery events with UTC time and trace
PASS  rejections: no token 401, wrong current 401, policy violation 400; password unchanged
PASS  initial administrator password replaced without email; the old one no longer authenticates
PASS  other renewable sessions are revoked; the session whose cookie accompanied the change keeps renewing
PASS  an access token issued before the change is still accepted by the consumers
PASS  ordinary user changes the password; without a cookie every session of the user is revoked
PASS  restart: the new administrator secret persists, admin is not restored, the kept session still renews
PASS  auth-api logs hold no passwords, tokens, cookies, or hashes, and record the change events with UTC time and trace
PASS  login: unchanged token body plus a restrictive auth_refresh cookie; failed login sets none
PASS  refresh: exact Origin required (403 otherwise); rotation issues a new cookie and token body
PASS  replay: the consumed credential is 401 and the whole family is revoked
PASS  concurrent refresh: at most one request succeeded
PASS  logout: 204, cookie cleared, family revoked, idempotent for every cookie state
PASS  administrative revocation: 401/403/404 conventions; every family of the user is revoked
PASS  disablement: families revoked and not restored by enable; refused last-admin disable keeps its family
PASS  consumers still accept the unexpired access token locally after revocation
PASS  restart: active families still refresh and revoked ones stay revoked
PASS  auth-api logs hold no secrets and record replay, logout, administrator, and disablement events
PASS  access control (401 anonymous, 200 administrator) and user creation/read-back
PASS  disable refuses login with a body identical to a wrong password; enable restores it
PASS  role created and assigned; the new token is 403 on the admin API and carries Operator at api-a
PASS  deleting an assigned role is refused (409); after removal it is deleted (204)
PASS  the sole enabled administrator cannot be disabled or lose its role (409, unchanged)
PASS  restart: users, emails, and enabled state persisted; the administrator was not re-created
PASS  auth-api logs contain no passwords, access tokens, or private key material
PASS  api-a and api-b accept a real token with identical subject (7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02) and roles
PASS  api-a and api-b authorize the administrator token on the role-restricted endpoint (401 without a token)
PASS  auth-api stopped: both consumers still accept the still-valid token (local validation)
PASS  private key absent from api-a and api-b (only the public key file is mounted)
PASS  api-a and api-b return 401 with an empty body for a missing token and a tampered token
PASS  empty-storage startup, SQLite creation, ready, login
PASS  restart: ready, login, stable subject
PASS  down -v: SQLite and RSA key survived, stable subject and fingerprint (0bd91a16b9ab90465ec04e12c0a6bd48644a1195c33bc6dad0116d347d402fa6)
PASS  initialization failure: container exited, never ready, no secrets in logs
PASS  missing signing key: container exited, never ready, setting named, no secrets in logs
Phase 1 acceptance: ALL PASS
PASS  Phase 1 acceptance regression
Phase 2 acceptance: ALL PASS
PASS  Phase 2 acceptance regression (includes Phase 1)
Phase 3 acceptance: ALL PASS
PASS  Phase 3 acceptance regression (includes Phases 2 and 1)
Phase 4 acceptance: ALL PASS
PASS  Phase 4 acceptance regression (includes Phases 3, 2 and 1)
Phase 5 acceptance: ALL PASS
PASS  Phase 5 acceptance regression (includes Phases 4, 3, 2 and 1)
Phase 6 acceptance: ALL PASS
PASS  Phase 6 acceptance regression (includes Phases 5, 4, 3, 2 and 1)
Phase 7 acceptance: ALL PASS
PASS  Phase 7 acceptance regression (includes Phases 6, 5, 4, 3, 2 and 1)
Phase 8 acceptance: ALL PASS
```
