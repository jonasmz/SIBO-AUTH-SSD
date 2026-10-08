# Phase 1 Operations

Phase 1 runs a single container, `auth-api`. It applies its own EF Core migrations and
creates the initial Identity state at startup; there is no migration or bootstrap service and
`dotnet ef database update` is never run by Compose.

## External configuration

Copy `.env.example` to `.env` and set the values. Configuration is bound from environment
variables (`Persistence__ConnectionString`, `Jwt__Issuer`, `Jwt__Audience`,
`Jwt__AccessTokenLifetimeMinutes`, `Jwt__PrivateKeyPath`). Missing or unreadable required
values terminate startup without printing secrets.

## Persistent storage

- **SQLite**: `AUTH_SQLITE_HOST_PATH` is bind-mounted at `/var/lib/auth-api/data`. The file
  `auth.db` is created there on first start.
- **RSA private key**: place `jwt-private.pem` in `AUTH_RSA_HOST_PATH` (mounted read-only at
  `/var/lib/auth-api/keys`). It must be readable by the container's non-root user (UID 1654 in the
  official image): on real hosts `chown 1654:1654` the file and directory and keep them `0600`/`0700`.
  Disposable acceptance runs use `0644`/`0755` instead. The key never enters the repository or the image.
- **Public key**: generate `jwt-public.pem` from the private key
  (`openssl pkey -in jwt-private.pem -pubout`) for verification; the service only needs the private key.
- The SQLite directory must be writable by that same UID, for example
  `install -d -m 0700 -o 1654 -g 1654 "$AUTH_SQLITE_HOST_PATH"` (or `chown` it after creation).

Neither asset uses a Compose-managed named volume, so `docker compose down -v` does not remove them.

## Health

- `GET /health/live` → `200 {"status":"healthy"}` while the process runs.
- `GET /health/ready` → `200 {"status":"healthy"}` after successful initialization while SQLite is
  reachable; otherwise a generic `503` ProblemDetails.

If migration, bootstrap, or configuration validation fails, the process exits before accepting
traffic and never reports ready.

## First access

The built-in administrator is created as `admin@local.invalid` with the password `admin`.
**This is an intentionally weak bootstrap credential and must be replaced after first access.**
Password replacement is delivered in a later roadmap phase; Phase 1 does not implement it.

## Gate G1 verification evidence

Recorded on 2026-10-07 against the working tree of branch `001-phase-1-bootstrap-identity-login-jwt`
(disposable storage only; nothing under `baseline/` was modified).

| Evidence | Command | Result |
|---|---|---|
| Build | `dotnet build Authentication.slnx` | PASS — 0 warnings, 0 errors |
| Automated tests | `dotnet test --solution Authentication.slnx` | PASS — 19/19 (18 integration, 1 unit), 0 skipped |
| Empty-storage startup, internal migrations/bootstrap | `tests/acceptance/phase-1.sh` | PASS |
| Login / RS256 JWT issuance | `LoginAndJwtTests` + acceptance login | PASS |
| Liveness / readiness | `BootstrapAndHealthTests` + acceptance probes | PASS |
| Failed initialization never reports ready, no secrets in logs | `BootstrapAndHealthTests` + acceptance (unwritable data directory, container exits) | PASS |
| Restart without duplicate/overwritten identity | `BootstrapLifecycleTests` + acceptance restart | PASS |
| `docker compose down -v` survival of SQLite and RSA key, stable key fingerprint | acceptance | PASS |
| Governance inspection (no secrets, forbidden packages, InMemory tests, external migration, future endpoints, extra services/volumes) | manual grep review of `src/`, `tests/`, `compose.yml`, `.env.example`, Dockerfile | PASS — only `/api/auth/login`, `/health/live`, `/health/ready`; one `auth-api` service; no named volumes |

Acceptance output (abridged):

```text
PASS  empty-storage startup, SQLite creation, ready, login
PASS  restart: ready, login, stable subject
PASS  down -v: SQLite and RSA key survived, stable subject and fingerprint
PASS  initialization failure: container exited, never ready, no secrets in logs
PASS  missing signing key: container exited, never ready, setting named, no secrets in logs
Phase 1 acceptance: ALL PASS
```
