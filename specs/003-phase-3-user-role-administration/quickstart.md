# Quickstart: Phase 3 Validation (Gate G3)

This guide validates the Phase 3 administrative capability end to end and re-verifies Phases 1–2.
For endpoint shapes, see
[contracts/authentication-api-admin.openapi.yaml](contracts/authentication-api-admin.openapi.yaml).
For rules and states, see [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK pinned by `global.json`
- Docker with Compose v2, `openssl`, `curl`, `jq` for the acceptance script
- No external services; all state is disposable

## New Configuration

| Setting | Compose variable | Rule |
|---|---|---|
| `Jwt:ClockSkewSeconds` (Authentication API) | `AUTH_JWT_CLOCK_SKEW_SECONDS` (already used by `api-a`/`api-b`) | Required, integer 0–60, reference value 30. Startup fails, naming only the setting, when missing or out of range |

No other setting, mount, or service is added. Existing databases migrate automatically at startup,
and the built-in administrator stays enabled.

## 1. Build

```bash
dotnet build Authentication.slnx
```

**Expected**: success with 0 warnings.

## 2. Automated Tests

```bash
dotnet test --solution Authentication.slnx
```

**Expected**: all tests pass, including the Phase 1 and Phase 2 scenario classes with their assertions unchanged (only a
`UserManager` type reference changes in the two bootstrap classes). The
Phase 3 scenarios prove:

| Scenario | Proves |
|---|---|
| Administrative access | no token / forged / expired beyond tolerance / wrong issuer / wrong audience / non-RS256 → `401` problem with `WWW-Authenticate: Bearer`; real non-admin token → `403` problem; admin token → `200` on all eleven operations |
| User lifecycle | create (`201`, default enabled), duplicate email by case → `409`, weak password → `400`, list/get show only permitted fields, email update changes the login email, extra attribute in `PATCH`, `PATCH {}`, malformed JSON, or non-JSON content type → `400`, unknown id → `404`, disable → login `401` identical to wrong password, enable → login `200` with the unchanged password, lockout visible in the view right after repeated failures (system clock) |
| Roles and assignments | create, duplicate (case-insensitive) → `409`, `PUT` roles replaces the set and the next login's token carries exactly those roles, a nonexistent role → `400` with nothing applied, rename keeps assignments and new tokens carry the new name, delete unassigned → `204`, delete assigned → `409`, an old token keeps its original roles |
| Administrator continuity | sole enabled admin: disable / remove `Administrator` / rename or delete `Administrator` → `409` with no change; a disabled second admin does not count; with two enabled admins, one can be disabled; two concurrent disables of two admins → exactly one `200` and one `409` (never `503`) |
| Unit | `AdministratorContinuity` truth table; `Jwt:ClockSkewSeconds` bounds |

## 3. Compose Demonstration

```bash
tests/acceptance/phase-3.sh
```

The script uses disposable host directories and a dedicated Compose project, then:

1. Starts `auth-api`, `api-a`, and `api-b` and logs in as `admin@local.invalid` / `admin`.
2. Calls `GET /api/admin/users` without a token (`401`) and with the admin token (`200`).
3. Creates role `Operator`, then user `operator@example.test` with that role. Logs in as that user,
   gets `403` on `/api/admin/users`, and gets `200` from `api-a` `/api/caller` with role `Operator`.
4. Disables the user. Login returns `401` with a body identical to a wrong-password login. Enables
   the user, and login returns `200` again.
5. Shows that deleting `Operator` while assigned returns `409`. Sets the user's roles to `[]`,
   then deletes `Operator` (`204`).
6. Shows that disabling the sole enabled administrator returns `409`.
7. Restarts `auth-api` and shows that the user, its email, and its enabled state persisted and
   that the administrator was not re-created.
8. Scans `docker compose logs auth-api` for the passwords used, every issued access token, and
   `PRIVATE KEY`. None may appear (NFR-002).
9. Runs `docker compose down -v`, then runs `tests/acceptance/phase-2.sh` with its own variables
   unset. That script also runs `phase-1.sh`, as regression.

**Expected**: every step prints `PASS` and the script exits `0`.

## 4. Manual Spot Check (optional)

```bash
TOKEN=$(curl -s localhost:8080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid","password":"admin"}' | jq -r .accessToken)
curl -s localhost:8080/api/admin/users -H "Authorization: Bearer $TOKEN" | jq
curl -s -X PATCH localhost:8080/api/admin/users/<id> -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"email":"x@example.test","enabled":false}' | jq   # 400
```

## Gate G3 Checklist

```text
build        PASS   (step 1, 0 warnings)
tests        PASS   (step 2)
startup      PASS   (step 3.1, migration applied)
feature      PASS   (steps 3.2–3.8)
regression   PASS   (step 3.9 + Phase 1–2 test classes)
```

Also confirm: no refresh, session, revocation, password-reset, or email artifacts exist. Record
the evidence in `docs/phase-3-operations.md` and create the closing commit per RD-007.
