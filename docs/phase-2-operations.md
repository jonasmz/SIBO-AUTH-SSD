# Phase 2 Operations

Phase 2 adds two services to Compose, `api-a` and `api-b`. Both run the same minimal reference
consumer (`src/ReferenceConsumer.Api`, authorized by Technical Constraints §5.2 amendment 1.1)
and stand in for Business API A and B. Each validates the access tokens issued by `auth-api`
**locally**, with a public key, and never calls Authentication API per request.

## Configuration

Set the variables from `.env.example` (see also `docs/phase-1-operations.md`):

| Variable | Purpose |
|---|---|
| `AUTH_JWT_ISSUER`, `AUTH_JWT_AUDIENCE` | Same values given to `auth-api`; one shared audience is expected by both consumers. |
| `AUTH_JWT_PUBLIC_KEY_HOST_FILE` | Host path of `jwt-public.pem`, mounted read-only into each consumer. |
| `AUTH_JWT_CLOCK_SKEW_SECONDS` | Expiry clock tolerance, 0–60 seconds (reference value 30); identical for both consumers. |
| `API_A_HTTP_PORT`, `API_B_HTTP_PORT` | Host ports (development/acceptance convenience only). |

Derive the public key from the Phase 1 private key:

```bash
openssl pkey -in jwt-private.pem -pubout -out jwt-public.pem
```

## Private key exclusion

Only `jwt-public.pem` is mounted into the consumers. They are never given the RSA key directory
or the SQLite directory, and a consumer refuses to start when the file it is given contains a
private key. Publishing the consumer ports on the host is a development and acceptance
convenience; the production rule that backends are not published directly, and the reverse
proxy, belong to Phase 8.

## Behavior

- `GET /api/caller` requires a valid bearer token and returns `{ service, subject, roles }`.
- `GET /api/caller/administrator` requires a valid token carrying the `Administrator` role and
  returns the same shape; a valid token without the role gets `403`, and no valid token gets `401`.
  Rejections carry no body and `WWW-Authenticate: Bearer` without error detail.
- `GET /health/live` is anonymous and returns `{"status":"healthy"}`.
- `tests/acceptance/phase-2.sh` demonstrates all of this on Compose with disposable storage and
  finishes by running `tests/acceptance/phase-1.sh` as regression.
- A consumer does not depend on `auth-api` at startup or per request: a token that is still valid
  keeps being accepted while Authentication API is stopped.
- Missing or invalid `Jwt` settings, or an unusable public key, stop the consumer at startup with
  a message that names only the setting.

## Gate G2 verification evidence

Recorded on 2026-10-07 against the working tree of branch `002-jwt-validation-consumer-apis`
(disposable storage only; `baseline/` was not modified by this verification).

| Evidence | Command | Result |
|---|---|---|
| Build | `dotnet build Authentication.slnx --no-incremental` | PASS — 0 warnings, 0 errors |
| Automated tests (Phase 1 + Phase 2) | `dotnet test --solution Authentication.slnx` | PASS — 31/31, 0 skipped |
| Both APIs validate JWT locally; same subject and roles | `ConsumerValidationTests` + `tests/acceptance/phase-2.sh` | PASS |
| Both APIs reject incorrect tokens (`401`, empty body, no challenge detail) | `ConsumerValidationTests` (no credential, non-bearer, malformed, forged, unsigned, non-RS256, expired, wrong issuer, wrong audience) + acceptance (missing and tampered token) | PASS |
| Clock tolerance honored (10 s late accepted, 90 s late rejected) | `ConsumerValidationTests` | PASS |
| Role authorization: `Administrator` → `200`, other/no role → `403`, no token → `401` | `ConsumerValidationTests` + acceptance (administrator token `200`) | PASS |
| Auth API stopped; still-valid token still accepted by both | `ConsumerValidationTests` + acceptance (`docker compose stop auth-api`) | PASS |
| Private key absent from consumers (only the public key file is mounted; no key material in the container) | acceptance | PASS |
| Startup fails naming only the setting (issuer, audience, clock skew, key file, private key as public key) | `ConsumerValidationTests` | PASS |
| Phase 1 regression | full suite + `tests/acceptance/phase-1.sh` (run by `phase-2.sh`, with no consumer variables set) | PASS |
| Governance inspection | `src/Authentication.*` unchanged vs `main`; `JwtBearer` the only new package; consumer has no `Authentication.*` reference, Authority/JWKS, persistence or HTTP client; Compose has only `auth-api`, `api-a`, `api-b`, no proxy, frontend or named volume; RSA and SQLite mounted only into `auth-api` | PASS |

Acceptance output:

```text
PASS  api-a and api-b accept a real token with identical subject and roles
PASS  api-a and api-b authorize the administrator token on the role-restricted endpoint (401 without a token)
PASS  auth-api stopped: both consumers still accept the still-valid token (local validation)
PASS  private key absent from api-a and api-b (only the public key file is mounted)
PASS  api-a and api-b return 401 with an empty body for a missing token and a tampered token
Phase 1 acceptance: ALL PASS
PASS  Phase 1 acceptance regression
Phase 2 acceptance: ALL PASS
```

Note: the Phase 1 test for an unreadable signing key skips itself when run as root or on Windows,
where file permissions cannot deny access; it runs in the standard Linux user environment used here.
