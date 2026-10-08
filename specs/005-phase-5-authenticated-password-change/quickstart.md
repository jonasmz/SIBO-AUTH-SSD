# Quickstart: Validate Phase 5 Authenticated Password Change

This guide is the Gate G5 validation plan. It references the public contract in
[`contracts/authentication-api-password.openapi.yaml`](contracts/authentication-api-password.openapi.yaml)
and the state rules in [data-model.md](data-model.md). Executed evidence is recorded in
`docs/phase-5-operations.md`.

## 1. Prerequisites

- .NET SDK 10.0.112 (or a later 10.0 patch selected by `global.json`).
- Docker Engine with Compose, `curl`, `jq`, `openssl` for acceptance.
- The Phase 1–4 configuration (`Jwt__*`, `Sqlite__ConnectionString`, `Security__FrontendOrigin`).
  No SMTP or email setting exists or is needed.

Use only disposable databases and keys: the acceptance script creates them under `$TMPDIR`.

## 2. Build and automated tests

```bash
dotnet build Authentication.slnx
dotnet test Authentication.slnx --no-build
dotnet ef migrations has-pending-model-changes \
  --project src/Authentication.Infrastructure --startup-project src/Authentication.Api
```

Expected: zero warnings, all tests pass, and no pending model changes (Phase 5 has no migration).
The suite must contain consolidated scenarios (NFR-001) proving:

| # | Scenario | Expected |
|---|---|---|
| 1 | No / invalid / expired bearer token | `401` + `WWW-Authenticate: Bearer`; password unchanged |
| 2 | Non-JSON, malformed JSON, missing or blank field | `400 The request is invalid.`; nothing changed |
| 2b | Valid body plus another user's `userId`/`email` | only the caller's password changes; the other user's password still logs in |
| 3 | Incorrect current password | `401 Invalid credentials.`, no `WWW-Authenticate`; password and sessions unchanged; `AccessFailedCount` incremented by one (NFR-SEC-BF-001); after `MaxFailedAccessAttempts` (effective `IdentityOptions`) consecutive failures login is locked out |
| 4 | New password violating the configured policy (e.g. `Identity__Password__RequiredLength` raised in the test host) | `400` policy detail; nothing changed |
| 5 | Valid change by a non-admin user | `204`; old password login → `401`; new password login → `200`; email, roles, enabled state unchanged |
| 6 | Two families + change with the cookie of family A | A still refreshes (`200`); B refresh → `401`; B's row has reason `PasswordChanged` |
| 7 | Change with no / malformed / unknown / expired (controlled time) / revoked / other user's cookie | every family of the user revoked; the other user's family untouched |
| 8 | Failed change (cases 1–4) with two families | both still refresh |
| 9 | Access token issued before the change | still accepted by `ReferenceConsumer.Api` until expiry |
| 10 | Built-in administrator (`admin@local.invalid` / `admin`) changes password | `204`, no email; still the only enabled Administrator; role and enabled state unchanged |
| 11 | Restart on the same temporary SQLite file after case 10 | new password `200`; `admin` → `401` |
| 12 | Two concurrent changes for one user (`Task.WhenAll`, no sleeps) | exactly one `204`, one `401`; only the winning new password logs in |
| 13 | Password write fails (temporary `RAISE(ABORT)` trigger on `AspNetUsers`, dropped afterwards) | generic `503`; after dropping the trigger the old password still works and sessions are intact |
| 13b | Revocation write fails after the password update (temporary trigger on `RenewableSessionFamilies`) | generic `503`; after dropping the trigger the old password still works, the new one does not, and both families still refresh (FR-010) |
| 14 | Log capture across cases 3, 5, 6 | one change event with user id, revoked count, UTC time, trace id; no password, hash, stamp, token or cookie value |
| 15 | Every response body of cases 1–13b | contains neither the submitted current nor new password |

Phase 1–4 test classes must pass unchanged (regression).

## 3. Manual smoke test (local)

```bash
BASE=http://localhost:8080
TOKEN=$(curl -s -c jar -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid","password":"admin"}' $BASE/api/auth/login | jq -r .accessToken)

curl -s -o /dev/null -w '%{http_code}\n' -b jar -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"currentPassword":"admin","newPassword":"<new secret>"}' $BASE/api/auth/change-password
# 204
```

Then: login with `admin` → `401`; login with the new secret → `200`; refresh with `jar` (and the
configured `Origin`) → `200`.

## 4. Acceptance (Compose)

```bash
tests/acceptance/phase-5.sh
```

`phase-5.sh` must: start the stack on disposable storage; change the initial administrator
password; prove a second admin session can no longer refresh while the current one can; run
`docker compose restart auth-api`; prove the new secret logs in and `admin` does not; prove an
ordinary user can change their password; confirm `auth-api` logs contain neither password; tear
down; and finally run `tests/acceptance/phase-4.sh`, which chains Phases 3, 2 and 1 as regression.
Expected: every script prints PASS.

## 5. Gate G5 checklist

Record in `docs/phase-5-operations.md`: build, tests, startup, feature and Phase 1–4 regression all
PASS; complete change operation; initial administrator credential retired and not restored;
integrated session revocation; no SMTP/`IEmailSender`/reset/forgot code or package
(`grep -rn -E 'IEmailSender|MailKit|forgot|reset-password' src` returns nothing); closing commit.
