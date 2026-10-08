# Phase 8 Operations

Phase 8 integrates the system into one production topology: `frontend` (Nginx, the only external entry
point), `auth-api`, `api-a` and `api-b`. This guide covers persistence and recovery first. The deployment
procedure, logging, OpenAPI exposure, acceptance instructions and Gate G8 evidence are added in the
closing tasks (T033–T036).

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
