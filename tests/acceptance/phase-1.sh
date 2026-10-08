#!/usr/bin/env bash
# Phase 1 disposable lifecycle demonstration (Gate G1 deployment evidence).
# Proves: empty-storage startup, restart, `docker compose down -v` survival of the external
# SQLite file and RSA key, stable key fingerprint, ready/login after each step, and that an
# unwritable data directory fails initialization without ever reporting ready.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

PORT="${AUTH_HTTP_PORT:-18080}"
BASE="http://localhost:${PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase1-acceptance.XXXXXX")"
FAILURE_STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase1-unwritable.XXXXXX")"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_HTTP_PORT="$PORT"
export COMPOSE_PROJECT_NAME="auth-api-phase1-acceptance"

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; exit 1; }

cleanup() {
  docker compose down -v >/dev/null 2>&1 || true
  chmod -R u+rwx "$FAILURE_STATE" 2>/dev/null || true
  rm -rf "$STATE" "$FAILURE_STATE"
}
trap cleanup EXIT

wait_ready() {
  for _ in $(seq 1 40); do
    if curl --fail --silent "$BASE/health/ready" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

login_subject() {
  local response token
  response="$(curl --fail --silent --request POST "$BASE/api/auth/login" \
    --header 'Content-Type: application/json' \
    --data '{"email":"admin@local.invalid","password":"admin"}')" || return 1
  token="$(jq -r .accessToken <<<"$response")"
  [ -n "$token" ] && [ "$token" != null ] || return 1
  # Decode the (base64url) payload for the stable subject; signature is verified by the xUnit suite.
  local payload="${token#*.}"; payload="${payload%%.*}"
  payload="$(printf '%s' "$payload" | tr '_-' '/+')"
  while [ $(( ${#payload} % 4 )) -ne 0 ]; do payload="${payload}="; done
  printf '%s' "$payload" | base64 -d | jq -r .sub
}

fingerprint() {
  openssl pkey -pubin -in "$STATE/keys/jwt-public.pem" -outform DER | openssl dgst -sha256 | awk '{print $NF}'
}

# --- Disposable external storage -------------------------------------------------------
# The container runs as a different non-root UID, so the disposable directories are world
# accessible. Production hosts should instead chown them to the container UID (see docs).
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"
FINGERPRINT_BEFORE="$(fingerprint)"

# --- Empty startup ---------------------------------------------------------------------
[ ! -e "$STATE/data/auth.db" ] || fail "storage was not empty"
docker compose up --build -d >/dev/null
wait_ready || fail "service did not become ready from empty storage"
[ "$(curl --silent "$BASE/health/live")" = '{"status":"healthy"}' ] || fail "liveness payload"
[ "$(curl --silent "$BASE/health/ready")" = '{"status":"healthy"}' ] || fail "readiness payload"
test -s "$STATE/data/auth.db" || fail "SQLite file missing after startup"
SUBJECT_1="$(login_subject)" || fail "login after empty startup"
pass "empty-storage startup, SQLite creation, ready, login"

# --- Restart ---------------------------------------------------------------------------
docker compose restart auth-api >/dev/null
wait_ready || fail "service not ready after restart"
SUBJECT_2="$(login_subject)" || fail "login after restart"
[ "$SUBJECT_1" = "$SUBJECT_2" ] || fail "subject changed across restart"
pass "restart: ready, login, stable subject"

# --- down -v survival ------------------------------------------------------------------
docker compose down -v >/dev/null
test -s "$STATE/data/auth.db" || fail "SQLite file lost after down -v"
test -s "$STATE/keys/jwt-private.pem" || fail "private key lost after down -v"
docker compose up -d >/dev/null
wait_ready || fail "service not ready after down -v"
SUBJECT_3="$(login_subject)" || fail "login after down -v"
[ "$SUBJECT_1" = "$SUBJECT_3" ] || fail "subject changed across down -v"
[ "$FINGERPRINT_BEFORE" = "$(fingerprint)" ] || fail "signing key fingerprint changed"
pass "down -v: SQLite and RSA key survived, stable subject and fingerprint ($FINGERPRINT_BEFORE)"
docker compose down -v >/dev/null

# --- Initialization failure never reports ready ----------------------------------------
cp "$STATE/keys/jwt-private.pem" "$FAILURE_STATE/" 
chmod 0555 "$FAILURE_STATE"
AUTH_SQLITE_HOST_PATH="$FAILURE_STATE" docker compose up -d >/dev/null
sleep 8
if curl --fail --silent "$BASE/health/ready" >/dev/null 2>&1; then fail "reported ready despite unwritable storage"; fi
STATUS="$(docker compose ps --all --format '{{.State}}' auth-api)"
[ "$STATUS" = "exited" ] || fail "container state is '$STATUS', expected exited"
LOGS="$(docker compose logs auth-api 2>&1)"
grep -q 'initialization failed during migration' <<<"$LOGS" || fail "initialization failure stage not diagnosed"
if grep -qE 'BEGIN (RSA )?PRIVATE KEY|eyJ' <<<"$LOGS"; then fail "secret material in logs"; fi
pass "initialization failure: container exited, never ready, no secrets in logs"

docker compose down -v >/dev/null 2>&1 || true

# --- Missing signing key never reports ready -------------------------------------------
EMPTY_KEYS="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase1-nokey.XXXXXX")"
chmod 0755 "$EMPTY_KEYS"
AUTH_RSA_HOST_PATH="$EMPTY_KEYS" docker compose up -d >/dev/null
sleep 8
if curl --fail --silent "$BASE/health/ready" >/dev/null 2>&1; then fail "reported ready without a signing key"; fi
[ "$(docker compose ps --all --format '{{.State}}' auth-api)" = "exited" ] || fail "container did not exit without a signing key"
LOGS="$(docker compose logs auth-api 2>&1)"
grep -q "Jwt:PrivateKeyPath" <<<"$LOGS" || fail "missing signing key setting not diagnosed"
if grep -qE 'BEGIN (RSA )?PRIVATE KEY|eyJ' <<<"$LOGS"; then fail "secret material in logs"; fi
rm -rf "$EMPTY_KEYS"
pass "missing signing key: container exited, never ready, setting named, no secrets in logs"

echo "Phase 1 acceptance: ALL PASS"
