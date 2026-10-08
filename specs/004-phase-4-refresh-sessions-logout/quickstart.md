# Quickstart: Validate Phase 4 Renewable Sessions

This guide is the Gate G4 validation plan. It references the public contract in
[`contracts/authentication-api-sessions.openapi.yaml`](contracts/authentication-api-sessions.openapi.yaml)
and state rules in [data-model.md](data-model.md).

## 1. Prerequisites

- .NET SDK 10.0.112 or a compatible 10.0 patch selected by `global.json`.
- Docker Engine with Compose for acceptance verification.
- Existing Phase 1–3 signing keys and required environment configuration.
- Optional `RefreshSession__LifetimeDays` (defaults to `7`) and required
  `Security__FrontendOrigin` matching the browser origin used by refresh/logout requests. Local
  HTTP validation runs outside Production so the
  refresh cookie need not carry `Secure`; Production must carry it.

Do not use shared or production data. The acceptance script must use its disposable Auth API
database path and the repository's existing disposable key setup.

## 2. Build and Focused Tests

```bash
dotnet build Authentication.slnx --no-restore
dotnet test Authentication.slnx --no-build
```

Expected: zero build warnings and all unit/integration tests pass. Focused evidence must include:

- login creates exactly one family and an HttpOnly, SameSite=Strict cookie but failed login does not;
- valid refresh rotates once and keeps the existing response schema and family absolute expiry;
- malformed, unknown, expired, revoked, locked-user and disabled-user refresh all return the same
  `401` contract without a replacement cookie;
- replay revokes the family; the replacement can no longer refresh;
- two coordinated requests using one credential produce at most one `200` and never two usable
  continuations;
- logout is `204` and clears the cookie when repeated, absent or unusable;
- disablement and admin revocation cover all of the user's families, while a refused last-admin
  disable changes none;
- missing/malformed/mismatched Origin is rejected on refresh/logout, while the configured exact
  origin succeeds;
- `ControlledTimeProvider` drives expiry tests without waits;
- a temporary file SQLite test proves migration and revoked-family state across restart;
- API A/B still accept an unexpired JWT locally after its renewable family is revoked.

## 3. Compose Acceptance Gate

With the repository's normal Phase 4 test environment exported, run:

```bash
tests/acceptance/phase-4.sh
```

The consolidated script should:

1. Build/start the three existing services and wait on readiness.
2. Log in twice as one enabled user with separate cookie jars and confirm two independent families.
3. Refresh the first cookie once, confirm the response fields, cookie replacement and rejection of
   the original; then confirm replay makes the replacement unusable.
4. Use a fresh family to prove concurrent same-cookie requests yield no more than one success.
5. Prove unknown/expired, disabled and Identity-locked credentials all receive generic `401`.
6. Prove logout clears the cookie, prevents renewal, and remains `204` when repeated.
7. Prove the Administrator endpoint revokes both independently logged-in families, returns `404`
   for an unknown user and preserves established `401`/`403` behavior.
8. Prove accepted disablement revokes all families and enablement restores none; prove refusal to
   disable the last enabled Administrator leaves its family active.
9. Restart Auth API without replacing its host-mounted SQLite path and confirm revocation persists.
10. Present a JWT issued before logout/revocation to API A and API B and confirm both validate it
    locally until expiry, with no Auth API dependency per request.
11. Scan logs for all test passwords, issued access tokens, raw refresh cookies, token hashes and
    `PRIVATE KEY`; none may appear. Confirm replay/logout/revocation event records do appear.
12. Run the Phase 3 script, which transitively runs Phase 2 and Phase 1 regression checks.

Expected: every step prints `PASS`, the script exits `0`, and the services use no new database or
permanent component.

## 4. Manual Contract Spot Check (optional)

Use the configured origin verbatim; these commands assume local Development HTTP.

```bash
export AUTH_BASE_URL=http://localhost:8080
export AUTH_FRONTEND_ORIGIN=http://localhost:8080

curl -i -c /tmp/auth-phase4.cookies \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid","password":"admin"}' \
  "$AUTH_BASE_URL/api/auth/login"

curl -i -b /tmp/auth-phase4.cookies -c /tmp/auth-phase4.cookies \
  -H "Origin: $AUTH_FRONTEND_ORIGIN" \
  -X POST "$AUTH_BASE_URL/api/auth/refresh"

curl -i -b /tmp/auth-phase4.cookies -c /tmp/auth-phase4.cookies \
  -H "Origin: $AUTH_FRONTEND_ORIGIN" \
  -X POST "$AUTH_BASE_URL/api/auth/logout"
```

Inspect `Set-Cookie` after login/refresh for `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, and an
absolute expiry. Logout must emit an expired matching cookie and return `204`. A refresh with a
different or absent `Origin` must return `403` and issue no credential.

## Gate G4 Checklist

```text
build        PASS   (step 2, zero warnings)
tests        PASS   (step 2)
startup      PASS   (step 3.1, migration applied)
feature      PASS   (steps 3.2–3.11)
regression   PASS   (step 3.12 + existing integration tests)
```

Record the evidence in the Phase 4 operations/progress documentation and create the identifiable
closing commit required by Roadmap RD-007 before marking G4 complete.
