# Quickstart: Validate Phase 2 End to End

This guide defines Gate G2 evidence after implementation. It reuses the Phase 1 disposable
storage procedure and adds the two reference consumers (`api-a`, `api-b`). It does not validate
reverse-proxy routing, frontend integration, or final four-service acceptance (Phase 8).

## Prerequisites

As in Phase 1: .NET 10 SDK (`global.json`), Docker with Compose, OpenSSL, `curl`, `jq`.

## 1. Build and Focused Automated Verification

```bash
dotnet build Authentication.slnx --no-incremental
dotnet test --solution Authentication.slnx
```

Expected evidence:

- build succeeds without first-party warnings;
- all Phase 1 tests still pass (regression);
- the consumer scenario passes for both `api-a` and `api-b` configurations, covering: a token
  issued by Authentication API accepted with correct `sub`/roles; no token, forged signature,
  wrong algorithm/unsigned, expired beyond tolerance, wrong issuer, wrong audience, and malformed
  token → `401` without error detail; token without `Administrator` → `403` on the
  administrator endpoint and `200` on the caller endpoint; acceptance after the Authentication
  API host is stopped; startup failure for missing/invalid validation configuration and for a
  private-key PEM supplied as the public key.

## 2. Prepare Disposable External Storage

Follow Phase 1 quickstart §2 (data and keys directories, RSA pair). Additionally export:

```bash
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$AUTH_PHASE1_STATE/keys/jwt-public.pem"
export AUTH_JWT_CLOCK_SKEW_SECONDS=30
```

Inspect `compose.yml`: `api-a` and `api-b` mount only `AUTH_JWT_PUBLIC_KEY_HOST_FILE`,
read-only; neither mounts `AUTH_RSA_HOST_PATH` or the SQLite directory. No proxy, frontend,
migration, or bootstrap service exists.

## 3. Start and Obtain a Token

```bash
docker compose up --build -d
TOKEN="$(curl --fail --silent --request POST http://localhost:${AUTH_HTTP_PORT:-8080}/api/auth/login \
  --header 'Content-Type: application/json' \
  --data '{"email":"admin@local.invalid","password":"admin"}' | jq -r .accessToken)"
```

## 4. Validate Locally in Both Consumers

```bash
for port in ${API_A_HTTP_PORT:-8081} ${API_B_HTTP_PORT:-8082}; do
  curl --fail --silent -H "Authorization: Bearer $TOKEN" http://localhost:$port/api/caller | jq .
  curl --fail --silent -H "Authorization: Bearer $TOKEN" http://localhost:$port/api/caller/administrator | jq .
  curl --silent -o /dev/null -w '%{http_code}\n' http://localhost:$port/api/caller   # 401
done
```

Both report the same `subject` (the built-in administrator ID) and `roles: ["Administrator"]`,
with `service` `api-a` / `api-b` respectively. The `403` case is proven by the automated suite,
because Phase 2 has no way to obtain a real token without the `Administrator` role.

## 5. Authentication API Unavailable

```bash
docker compose stop auth-api
curl --fail --silent -H "Authorization: Bearer $TOKEN" http://localhost:${API_A_HTTP_PORT:-8081}/api/caller | jq .
curl --fail --silent -H "Authorization: Bearer $TOKEN" http://localhost:${API_B_HTTP_PORT:-8082}/api/caller | jq .
docker compose start auth-api
```

Both consumers keep accepting the still-valid token.

## 6. Private Key Absent from Consumers

```bash
docker compose exec api-a sh -c 'grep -rl "PRIVATE KEY" / 2>/dev/null | head -1'   # no output
docker compose exec api-b sh -c 'grep -rl "PRIVATE KEY" / 2>/dev/null | head -1'   # no output
docker inspect --format '{{range .Mounts}}{{.Source}} {{end}}' "$(docker compose ps -q api-a)"
```

Only the public key file appears as a mount.

## 7. Regression and Gate G2 Record

Run `tests/acceptance/phase-1.sh` and `tests/acceptance/phase-2.sh`. Record:

| Evidence | Required result |
|---|---|
| Build | PASS |
| Focused automated tests (Phase 1 + Phase 2) | PASS |
| Both APIs validate JWT locally | PASS |
| Both APIs reject incorrect tokens (`401`) | PASS |
| Role authorization (`403` vs `401`) | PASS |
| Auth API stopped; valid token still accepted | PASS |
| Private key absent from consumers | PASS |
| Phase 1 regression (`phase-1.sh`) | PASS |
| Checklist and operational documentation | Updated |
| Identifiable Phase 2 closing commit | Created |
