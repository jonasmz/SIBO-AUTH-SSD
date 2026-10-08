# Data Model: Phase 8 — Operations, Deployment Integration and Final Acceptance

No entity, table, column, or migration is added. The "data" of this phase is operational: the
persistent assets the deployment must keep, the log stream, backups, and the acceptance environment.

## Persistent assets (host storage, independent of the Compose lifecycle)

| Asset | Host variable → container path | Writer | Survives restart / rebuild / recreate / `down -v` | Removed only by |
|---|---|---|---|---|
| SQLite database `auth.db` | `AUTH_SQLITE_HOST_PATH` → `/var/lib/auth-api/data` | auth-api | yes | operator deleting the host path |
| Data Protection key ring | `AUTH_DATAPROTECTION_HOST_PATH` → `/var/lib/auth-api/dataprotection` | auth-api | yes | same |
| RSA private key `jwt-private.pem` | `AUTH_RSA_HOST_PATH` → `/var/lib/auth-api/keys` (ro) | operator | yes | same |
| RSA public key | `AUTH_JWT_PUBLIC_KEY_HOST_FILE` → consumers (ro) | operator | yes | same |
| Log files `auth-yyyy-MM-dd.log` | `AUTH_LOGS_HOST_PATH` → `/app/logs` | auth-api | yes | retention or operator |

Ownership and modes: [contracts/deployment-topology.md](contracts/deployment-topology.md).

## Operational log

- **Stream**: every enabled `ILogger` event → console line and file line (same event).
- **File state**: `current file date` (UTC); transitions `date(event) ≠ current → close, open
  auth-<date>.log, apply retention`.
- **Retention rule**: delete `auth-<d>.log` when `d < today(UTC) − RetentionDays`.
- **Line fields**: UTC timestamp, level, category, event id (optional), trace id, span id
  (optional), message, exception type + escaped stack trace (optional). Format:
  [contracts/operational-log.md](contracts/operational-log.md).

## Security events added

| Event | Fields | Trigger |
|---|---|---|
| `LoginSucceeded` | `UserId` | credentials accepted for an enabled, unlocked account |
| `UserCreated` | `UserId`, `RoleCount` | administrative creation committed |
| `UserEnabled` / `UserDisabled` | `UserId` | enabled state actually changed |
| `UserRoleAssigned` / `UserRoleRemoved` | `UserId`, `Role` | each role in the added/removed set of a committed replacement |

All with `OccurredAtUtc`, `TraceId`, `SpanId`; none with email, password, token, or hash.

## Backup

| Attribute | Rule |
|---|---|
| Source | live `auth.db`, possibly being written |
| Method | SQLite online backup API (`sqlite3 .backup`) |
| Validity | `PRAGMA integrity_check` returns `ok` |
| Restore target | stopped auth-api data directory (or a disposable one); journal/WAL side files removed |
| After restore | startup migrates older schemas forward, never recreates a current database, ensures the administrator idempotently |

## Acceptance environment

Disposable root under `$TMPDIR` holding data, key ring, keys, logs, frontend static test page, TLS
pair, and backups; its own `COMPOSE_PROJECT_NAME`; removed on exit. Never points at
`/srv/auth-system` or any operator path.
