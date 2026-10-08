# Phase 4 Operations

Phase 4 adds renewable browser sessions to Authentication API: a login-issued `auth_refresh`
cookie, refresh with rotation and replay containment, logout, administrative revocation, and
revocation on account disablement. No new service, volume, or storage: the same `auth-api`,
`api-a`, and `api-b` Compose services run as before; the new tables live in the existing SQLite
database and are created by a migration applied at startup.

## Configuration

| Variable | Purpose |
|---|---|
| `AUTH_FRONTEND_ORIGIN` | **Required.** Exact origin (scheme + host [+ port]) allowed to call `POST /api/auth/refresh` and `POST /api/auth/logout`. Any other, missing, or opaque `Origin` gets `403`. Maps to `Security__FrontendOrigin`; startup fails, without echoing the value, when it is missing or not an absolute origin. |
| `AUTH_REFRESH_SESSION_LIFETIME_DAYS` | Optional, default `7`. Positive whole number. Maps to `RefreshSession__LifetimeDays`. The absolute family expiry is fixed at login; rotation never extends it. |

The acceptance scripts export `AUTH_FRONTEND_ORIGIN=https://frontend.acceptance` by default.

## Behavior to know

- `POST /api/auth/login` still returns `{ accessToken, expiresAtUtc }` and additionally sets
  `auth_refresh` (`HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, no `Domain`, `Secure` in
  Production). Failed, disabled, and locked logins set no cookie.
- `POST /api/auth/refresh` needs the cookie and the exact `Origin`; no access token. Every unusable
  credential (unknown, malformed, expired, revoked, disabled or locked user) is the same `401`.
  Persistence failure is `503`. Success rotates the credential inside the same family.
- Presenting an already-consumed credential revokes the whole family (`Replay`).
- `POST /api/auth/logout` is idempotent: `204` and a clearing cookie for every credential state.
- `POST /api/admin/users/{id}/revoke-sessions` (Administrator): `204`, `401`, `403`, `404`, `503`.
- Disabling a user revokes all of their families atomically; enabling restores none. A refused
  last-administrator disable changes nothing.
- Already-issued access tokens are never revoked centrally: they remain valid locally at the
  consumer APIs until they expire.
- Logs record replay, logout, administrator, and disablement revocations with an ISO-8601 UTC time
  and trace/span ids; they never contain cookies, tokens, digests, or passwords.

## Verification commands

```bash
dotnet build
dotnet test                       # unit + integration (real SQLite)
tests/acceptance/phase-4.sh       # Compose lifecycle; also runs Phase 3, 2 and 1 acceptance
```

Requires `docker compose`, `openssl`, `curl`, `jq`.

## Gate G4 evidence (recorded 2026-10-08)

| State | Evidence | Result |
|---|---|---|
| Build | `dotnet build` | PASS, 0 warnings, 0 errors |
| Tests | `dotnet test` | PASS, 103 of 103 (unit and integration) |
| Startup | `phase-4.sh`: Compose stack ready, migration applied, restart keeps state | PASS |
| Feature | `phase-4.sh`: login cookie, Origin boundary, rotation, replay, concurrent refresh, logout, admin revocation, disablement, local consumer validation, secret-free logs with revocation events | PASS |
| Regression | `phase-4.sh` finishing with `phase-3.sh` → `phase-2.sh` → `phase-1.sh` | PASS (all four report ALL PASS) |

Focused test coverage lives in `tests/Authentication.IntegrationTests/Scenarios/` (`Renewable*`,
`Refresh*`, `Logout*`, `AdministrativeSessionRevocation*`, `UserDisableSessionRevocationTests`,
`ConsumerValidationTests`). The reusable validation guide remains
`specs/004-phase-4-refresh-sessions-logout/quickstart.md`.

Gate G4 approval by the project owner is **pending** (task T053).
