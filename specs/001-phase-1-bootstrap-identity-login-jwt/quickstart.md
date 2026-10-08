# Quickstart: Validate Phase 1 End to End

This guide defines Gate G1 evidence after implementation. Run lifecycle demonstrations only
against disposable Phase 1 storage. It covers the current SQLite, Identity, and RSA assets;
Phase 8 repeats final acceptance with later persistent state and the full four-service topology.

## Prerequisites

- .NET 10 SDK selected by `global.json`
- Docker with Docker Compose
- OpenSSL
- `curl` and `jq`
- A shell capable of exporting environment variables

## 1. Build and Focused Automated Verification

From the repository root:

```bash
dotnet build Authentication.slnx
dotnet test Authentication.slnx
```

Expected evidence:

- build succeeds without first-party compiler or analyzer warnings;
- all tests pass through Microsoft Testing Platform;
- migration/restart scenarios use temporary SQLite files;
- login/JWT tests use Identity, real SQLite, controlled time, and RSA verification;
- no skipped, placeholder, or future-capability tests exist.

The integration suite must consolidate these scenarios:

1. Empty-file startup, migrations, built-in role/user assignment, restart idempotence, and
   preservation of modified administrator state.
2. Valid login, equivalent unknown-email/wrong-password `401` responses, and failed-attempt
   accounting.
3. RS256 verification with the public key, required claims, stable `sub`, issuer/audience,
   UTC timestamps, 900-second default lifetime, configured lifetime, and matching response expiry.
4. Healthy liveness/readiness and deterministic initialization failure that never reports ready
   or leaks password, token, PEM, or configuration secrets.

## 2. Prepare Disposable External Storage

Choose an explicit host path outside the repository and outside Compose-managed volumes:

```bash
export AUTH_PHASE1_STATE=/tmp/auth-api-phase1-acceptance
# The container runs as a non-root UID that differs from your user; the disposable
# directories are therefore world accessible. On real hosts chown them to UID 1654 instead.
install -d -m 0777 "$AUTH_PHASE1_STATE/data"
install -d -m 0755 "$AUTH_PHASE1_STATE/keys"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 \
  -out "$AUTH_PHASE1_STATE/keys/jwt-private.pem"
openssl pkey -in "$AUTH_PHASE1_STATE/keys/jwt-private.pem" -pubout \
  -out "$AUTH_PHASE1_STATE/keys/jwt-public.pem"
chmod 0644 "$AUTH_PHASE1_STATE/keys/jwt-private.pem"  # disposable only; use 0600 + chown on real hosts
chmod 0644 "$AUTH_PHASE1_STATE/keys/jwt-public.pem"
export AUTH_SQLITE_HOST_PATH="$AUTH_PHASE1_STATE/data"
export AUTH_RSA_HOST_PATH="$AUTH_PHASE1_STATE/keys"
```

Inspect `compose.yml` before startup. It must mount the SQLite directory from
`AUTH_SQLITE_HOST_PATH`, mount the private key from `AUTH_RSA_HOST_PATH` read-only where
supported, and contain no migration/bootstrap service or `dotnet ef` command. The private key
must not appear in the repository, image, Compose environment values, or versioned configuration.

## 3. Start from Empty Storage

```bash
docker compose up --build -d
docker compose ps
curl --fail --silent http://localhost:${AUTH_HTTP_PORT:-8080}/health/live | jq .
curl --fail --silent http://localhost:8080/health/ready | jq .
```

Both health calls must return:

```json
{
  "status": "healthy"
}
```

The data directory must now contain the SQLite file. No operator-run migration or bootstrap
command is permitted.

## 4. Authenticate the Built-in Administrator

```bash
curl --fail --silent \
  --request POST http://localhost:8080/api/auth/login \
  --header 'Content-Type: application/json' \
  --data '{"email":"admin@local.invalid","password":"admin"}' | jq .
```

Expected response fields:

```json
{
  "accessToken": "<RS256 JWT>",
  "expiresAtUtc": "<UTC date-time>"
}
```

> **First-access security requirement:** `admin` is an intentionally weak bootstrap credential
> and must be replaced after first access. Password replacement is delivered in Roadmap Phase 5;
> Phase 1 communicates this requirement but does not implement or simulate that later workflow.

Use the automated JWT scenario for cryptographic verification rather than treating decoded
claims as proof of signature validity. Record the public-key fingerprint for lifecycle comparison:

```bash
openssl pkey -pubin -in "$AUTH_PHASE1_STATE/keys/jwt-public.pem" -outform DER |
  openssl dgst -sha256
```

Invalid email and password attempts must both return the contract's identical generic `401`
ProblemDetails. The response and container logs must not reveal the password, token, private
key, account existence, Identity internals, database path, or stack trace.

## 5. Restart and Recreate

```bash
docker compose restart auth-api
curl --fail --silent http://localhost:8080/health/ready | jq .
```

Login must still succeed, the integration suite must show that no administrator/role duplication
or overwrite occurred, and the public-key fingerprint must remain unchanged.

Then prove the current external assets survive removal of Compose-managed volumes:

```bash
docker compose down -v
test -s "$AUTH_PHASE1_STATE/data/auth.db"
test -s "$AUTH_PHASE1_STATE/keys/jwt-private.pem"
docker compose up -d
curl --fail --silent http://localhost:8080/health/ready | jq .
```

Login must succeed again with the same stable subject and signing-key fingerprint. This is a
Phase 1-scoped demonstration, not the final Phase 8 teardown acceptance.

## 6. Demonstrate Initialization Failure

Use a second disposable location that the non-root container cannot write
(`tests/acceptance/phase-1.sh` automates sections 2–6):

```bash
export AUTH_FAILURE_STATE=/tmp/auth-api-phase1-unwritable
install -d -m 0555 "$AUTH_FAILURE_STATE"
AUTH_SQLITE_HOST_PATH="$AUTH_FAILURE_STATE" docker compose up auth-api
```

The service must fail startup before it can report successful readiness or accept normal traffic.
Diagnostics must identify the initialization class of failure without exposing secrets. Restore
the original `AUTH_SQLITE_HOST_PATH` before continuing.

## 7. Gate G1 Record

Record the following before closing Phase 1:

| Evidence | Required result |
|---|---|
| Build | PASS |
| Focused automated tests | PASS |
| Startup from empty external storage | PASS |
| Internal migrations; no external migration/bootstrap stage | PASS |
| Initial administrator and `Administrator` role | PASS |
| Email/password login and RS256 JWT | PASS |
| Liveness/readiness, including failed-startup evidence for initialization failure | PASS |
| Restart/recreation without duplicate or overwritten identity | PASS |
| External SQLite and RSA persistence, including scoped `down -v` | PASS |
| Regression | PASS |
| Phase checklist and operational documentation | Updated |
| Identifiable Phase 1 closing commit | Created |

Do not claim Phase 2–8 capabilities from this validation. In particular, do not validate Business
API JWT consumption, refresh/logout, administration, password flows, rate limiting, Data
Protection, proxy hardening, backup/restore, or final four-service acceptance here.
