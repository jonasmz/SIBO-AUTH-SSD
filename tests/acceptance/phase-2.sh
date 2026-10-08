#!/usr/bin/env bash
# Phase 2 disposable Compose demonstration (Gate G2 deployment evidence).
# Proves: both reference consumers (api-a, api-b) accept a real Authentication API token with the
# same caller identity, keep accepting it while auth-api is stopped (local validation), reject
# a missing or tampered token, and never receive the private signing key. Finishes by running
# tests/acceptance/phase-1.sh as regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase2-acceptance.XXXXXX")"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$STATE/keys/jwt-public.pem"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_FRONTEND_ORIGIN="${AUTH_FRONTEND_ORIGIN:-https://frontend.acceptance}"
# Phase 6 settings (required by compose.yml): a disposable key-ring directory and a dummy SMTP sender
# that these scripts never use.
export AUTH_DATAPROTECTION_HOST_PATH="$STATE/dataprotection"
export AUTH_SMTP_HOST="${AUTH_SMTP_HOST:-smtp.acceptance.invalid}"
export AUTH_SMTP_PORT="${AUTH_SMTP_PORT:-2525}"
export AUTH_SMTP_SECURITY="${AUTH_SMTP_SECURITY:-None}"
export AUTH_SMTP_SENDER_ADDRESS="${AUTH_SMTP_SENDER_ADDRESS:-no-reply@acceptance.invalid}"
export AUTH_SMTP_SENDER_NAME="${AUTH_SMTP_SENDER_NAME:-Authentication API Acceptance}"
# Phase 7 limits are lifted for regression runs; phase-7.sh sets its own small values.
export AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT="${AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT="${AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT="${AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT="${AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT="${AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT:-100000}"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_JWT_CLOCK_SKEW_SECONDS="30"
export AUTH_HTTP_PORT="$AUTH_PORT"
export API_A_HTTP_PORT="$A_PORT"
export API_B_HTTP_PORT="$B_PORT"
export COMPOSE_PROJECT_NAME="auth-api-phase2-acceptance"
# Phase 8: disposable logs, frontend inputs, and the direct-access override for the final topology.
source "$REPO_ROOT/tests/acceptance/deployment-env.sh"

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; exit 1; }

cleanup() {
  docker compose down -v >/dev/null 2>&1 || true
  rm -rf "$STATE"
}
trap cleanup EXIT

wait_for() {
  local url="$1"
  for _ in $(seq 1 40); do
    if curl --fail --silent "$url" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

# Prints "<http_status>" for GET $url with an optional bearer token; the body goes to $BODY_FILE.
BODY_FILE="$STATE/body.out"
status_of() {
  local url="$1" token="${2:-}"
  if [ -n "$token" ]; then
    curl --silent --output "$BODY_FILE" --write-out '%{http_code}' --header "Authorization: Bearer $token" "$url"
  else
    curl --silent --output "$BODY_FILE" --write-out '%{http_code}' "$url"
  fi
}

# --- Disposable external storage (same shape as Phase 1) -------------------------------
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
install -d -m 0777 "$STATE/dataprotection"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"

# --- Start the three-service stack and obtain a real token -----------------------------
docker compose up --build -d >/dev/null
wait_for "http://localhost:${AUTH_PORT}/health/ready" || fail "auth-api did not become ready"
wait_for "http://localhost:${A_PORT}/health/live" || fail "api-a did not become live"
wait_for "http://localhost:${B_PORT}/health/live" || fail "api-b did not become live"
TOKEN="$(curl --fail --silent --request POST "http://localhost:${AUTH_PORT}/api/auth/login" \
  --header 'Content-Type: application/json' \
  --data '{"email":"admin@local.invalid","password":"admin"}' | jq -r .accessToken)"
[ -n "$TOKEN" ] && [ "$TOKEN" != null ] || fail "login did not return a token"

assert_caller() {
  local port="$1" service="$2"
  [ "$(status_of "http://localhost:${port}/api/caller" "$TOKEN")" = 200 ] || fail "$service rejected a valid token"
  [ "$(jq -r .service "$BODY_FILE")" = "$service" ] || fail "$service reported the wrong service name"
  [ "$(jq -r '.roles | join(",")' "$BODY_FILE")" = Administrator ] || fail "$service reported unexpected roles"
  jq -r .subject "$BODY_FILE"
}

# --- US1: local acceptance on both consumers -------------------------------------------
SUBJECT_A="$(assert_caller "$A_PORT" api-a)"
SUBJECT_B="$(assert_caller "$B_PORT" api-b)"
[ "$SUBJECT_A" = "$SUBJECT_B" ] || fail "consumers disagree on the caller subject"
pass "api-a and api-b accept a real token with identical subject ($SUBJECT_A) and roles"

# --- US3: the real administrator token reaches the role-restricted endpoint ------------
# The 403 case is proven by the automated suite: Phase 2 cannot issue a real non-administrator token.
for port_service in "$A_PORT:api-a" "$B_PORT:api-b"; do
  port="${port_service%%:*}"; service="${port_service##*:}"
  [ "$(status_of "http://localhost:${port}/api/caller/administrator" "$TOKEN")" = 200 ] || fail "$service denied the administrator token on /api/caller/administrator"
  [ "$(jq -r '.roles | join(",")' "$BODY_FILE")" = Administrator ] || fail "$service administrator endpoint reported unexpected roles"
  [ "$(status_of "http://localhost:${port}/api/caller/administrator")" = 401 ] || fail "$service did not return 401 on /api/caller/administrator without a token"
done
pass "api-a and api-b authorize the administrator token on the role-restricted endpoint (401 without a token)"

docker compose stop auth-api >/dev/null
[ "$(assert_caller "$A_PORT" api-a)" = "$SUBJECT_A" ] || fail "api-a stopped accepting while auth-api is down"
[ "$(assert_caller "$B_PORT" api-b)" = "$SUBJECT_B" ] || fail "api-b stopped accepting while auth-api is down"
pass "auth-api stopped: both consumers still accept the still-valid token (local validation)"
docker compose start auth-api >/dev/null

# --- US1: private key absent from consumers --------------------------------------------
for service in api-a api-b; do
  container="$(docker compose ps -q "$service")"
  mounts="$(docker inspect --format '{{range .Mounts}}{{.Source}}{{"\n"}}{{end}}' "$container" | sed '/^$/d')"
  [ "$mounts" = "$AUTH_JWT_PUBLIC_KEY_HOST_FILE" ] || fail "$service mounts more than the public key file"
  docker compose exec -T "$service" sh -c 'command -v grep >/dev/null' || fail "grep unavailable in $service"
  leaked="$(docker compose exec -T "$service" sh -c 'grep -rl "PRIVATE KEY" /var/lib /app /etc 2>/dev/null; true')"
  [ -z "$leaked" ] || fail "private key material found in $service: $leaked"
done
pass "private key absent from api-a and api-b (only the public key file is mounted)"

# --- US2: rejection over Compose ---------------------------------------------------------
# Tamper with the signature (first character of the third segment) so it no longer verifies.
HEADER_PAYLOAD="${TOKEN%.*}"
SIGNATURE="${TOKEN##*.}"
FIRST="${SIGNATURE:0:1}"
if [ "$FIRST" = "A" ]; then SWAP="B"; else SWAP="A"; fi
TAMPERED="${HEADER_PAYLOAD}.${SWAP}${SIGNATURE:1}"
for port_service in "$A_PORT:api-a" "$B_PORT:api-b"; do
  port="${port_service%%:*}"; service="${port_service##*:}"
  [ "$(status_of "http://localhost:${port}/api/caller")" = 401 ] || fail "$service did not return 401 without a token"
  [ ! -s "$BODY_FILE" ] || fail "$service returned a body for a request without a token"
  [ "$(status_of "http://localhost:${port}/api/caller" "$TAMPERED")" = 401 ] || fail "$service did not return 401 for a tampered token"
  [ ! -s "$BODY_FILE" ] || fail "$service returned a body for a tampered token"
done
pass "api-a and api-b return 401 with an empty body for a missing token and a tampered token"

docker compose down -v >/dev/null

# --- Regression: Phase 1 acceptance, with none of the consumer variables set -----------
(
  unset AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH \
        AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE
  "$REPO_ROOT/tests/acceptance/phase-1.sh"
) || fail "Phase 1 regression failed"
pass "Phase 1 acceptance regression"

echo "Phase 2 acceptance: ALL PASS"
