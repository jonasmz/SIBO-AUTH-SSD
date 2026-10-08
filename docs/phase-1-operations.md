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
  `/var/lib/auth-api/keys`). Keep the file `0600` and the directory `0700`; it must stay readable
  by the container's non-root user (UID 1654 in the official image) and never enters the repository
  or the image.
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
