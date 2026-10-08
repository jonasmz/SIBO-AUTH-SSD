#!/usr/bin/env bash
# Phase 3 disposable Compose demonstration (Gate G3 deployment evidence).
# Proves, over real Compose services on disposable external storage: administrative access
# control (401/403/200), user administration, enable/disable and its effect on login, role
# administration, the last-enabled-administrator protection, persistence across a restart, and
# that no secret reaches the auth-api logs. Finishes with the Phase 2 acceptance (which runs
# Phase 1) as regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
BASE="http://localhost:${AUTH_PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase3-acceptance.XXXXXX")"

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
export COMPOSE_PROJECT_NAME="auth-api-phase3-acceptance"

ADMIN_EMAIL="admin@local.invalid"
ADMIN_PASSWORD="admin"
USER_PASSWORD="Accept4nce!"

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

# status_of METHOD PATH [TOKEN] [JSON]: prints the HTTP status; the body goes to $BODY_FILE.
BODY_FILE="$STATE/body.out"
status_of() {
  local method="$1" path="$2" token="${3:-}" json="${4:-}"
  local args=(--silent --output "$BODY_FILE" --write-out '%{http_code}' --request "$method")
  [ -z "$token" ] || args+=(--header "Authorization: Bearer $token")
  [ -z "$json" ] || args+=(--header 'Content-Type: application/json' --data "$json")
  curl "${args[@]}" "$BASE$path"
}

login_token() {
  local email="$1" password="$2"
  curl --fail --silent --request POST "$BASE/api/auth/login" \
    --header 'Content-Type: application/json' \
    --data "$(jq -cn --arg e "$email" --arg p "$password" '{email:$e,password:$p}')" | jq -r .accessToken
}

# login_status EMAIL PASSWORD: prints the status and leaves the body in $BODY_FILE.
login_status() {
  curl --silent --output "$BODY_FILE" --write-out '%{http_code}' --request POST "$BASE/api/auth/login" \
    --header 'Content-Type: application/json' \
    --data "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
}

# --- Disposable external storage (same shape as Phases 1 and 2) -------------------------
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
install -d -m 0777 "$STATE/dataprotection"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"

# --- Start the three-service stack and obtain the administrator token --------------------
docker compose up --build -d >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api did not become ready"
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[ -n "$ADMIN_TOKEN" ] && [ "$ADMIN_TOKEN" != null ] || fail "administrator login did not return a token"

# --- US1: access control and user administration -----------------------------------------
[ "$(status_of GET /api/admin/users)" = 401 ] || fail "anonymous request to /api/admin/users was not 401"
[ "$(status_of GET /api/admin/users "$ADMIN_TOKEN")" = 200 ] || fail "administrator was not authorized on /api/admin/users"
[ "$(jq length "$BODY_FILE")" = 1 ] || fail "expected only the built-in administrator before creating users"

CREATED="$(jq -cn --arg e "operator@example.test" --arg p "$USER_PASSWORD" '{email:$e,password:$p}')"
[ "$(status_of POST /api/admin/users "$ADMIN_TOKEN" "$CREATED")" = 201 ] || fail "user creation failed"
USER_ID="$(jq -r .id "$BODY_FILE")"
[ "$(status_of GET "/api/admin/users/$USER_ID" "$ADMIN_TOKEN")" = 200 ] || fail "created user could not be read back"
[ "$(jq -r .email "$BODY_FILE")" = operator@example.test ] || fail "created user has the wrong email"
pass "access control (401 anonymous, 200 administrator) and user creation/read-back"

# --- US2: disable and re-enable, and their effect on login ---------------------------------
WRONG_STATUS="$(login_status operator@example.test "Not-The-Password1")"
WRONG_BODY="$(jq -S 'del(.traceId)' "$BODY_FILE")"
[ "$WRONG_STATUS" = 401 ] || fail "wrong-password login was not 401"
[ "$(login_status operator@example.test "$USER_PASSWORD")" = 200 ] || fail "the new user could not log in"

[ "$(status_of POST "/api/admin/users/$USER_ID/disable" "$ADMIN_TOKEN")" = 200 ] || fail "disable failed"
[ "$(jq -r .enabled "$BODY_FILE")" = false ] || fail "disabled user is still reported enabled"
DISABLED_STATUS="$(login_status operator@example.test "$USER_PASSWORD")"
DISABLED_BODY="$(jq -S 'del(.traceId)' "$BODY_FILE")"
[ "$DISABLED_STATUS" = 401 ] || fail "disabled user could still log in"
[ "$DISABLED_BODY" = "$WRONG_BODY" ] || fail "disabled-user login differs from a wrong-password login"

[ "$(status_of POST "/api/admin/users/$USER_ID/enable" "$ADMIN_TOKEN")" = 200 ] || fail "enable failed"
[ "$(login_status operator@example.test "$USER_PASSWORD")" = 200 ] || fail "re-enabled user could not log in with the same password"
pass "disable refuses login with a body identical to a wrong password; enable restores it"

# --- US3: roles and assignments ------------------------------------------------------------
[ "$(status_of POST /api/admin/roles "$ADMIN_TOKEN" '{"name":"Operator"}')" = 201 ] || fail "role creation failed"
ROLE_ID="$(jq -r .id "$BODY_FILE")"
[ "$(status_of PUT "/api/admin/users/$USER_ID/roles" "$ADMIN_TOKEN" '{"roles":["Operator"]}')" = 200 ] || fail "role assignment failed"
OPERATOR_TOKEN="$(login_token operator@example.test "$USER_PASSWORD")"
[ "$(status_of GET /api/admin/users "$OPERATOR_TOKEN")" = 403 ] || fail "a non-administrator was not refused with 403"
A_STATUS="$(curl --silent --output "$BODY_FILE" --write-out '%{http_code}' --header "Authorization: Bearer $OPERATOR_TOKEN" "http://localhost:${A_PORT}/api/caller")"
[ "$A_STATUS" = 200 ] || fail "api-a rejected the new token ($A_STATUS)"
[ "$(jq -r '.roles | join(",")' "$BODY_FILE")" = Operator ] || fail "api-a did not report the assigned role"
pass "role created and assigned; the new token is 403 on the admin API and carries Operator at api-a"

[ "$(status_of DELETE "/api/admin/roles/$ROLE_ID" "$ADMIN_TOKEN")" = 409 ] || fail "an assigned role was deleted"
[ "$(status_of PUT "/api/admin/users/$USER_ID/roles" "$ADMIN_TOKEN" '{"roles":[]}')" = 200 ] || fail "role removal failed"
[ "$(status_of DELETE "/api/admin/roles/$ROLE_ID" "$ADMIN_TOKEN")" = 204 ] || fail "an unassigned role could not be deleted"
pass "deleting an assigned role is refused (409); after removal it is deleted (204)"

# --- US4: the last enabled administrator is protected ----------------------------------------
BUILTIN_ADMIN_ID="7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02"
[ "$(status_of POST "/api/admin/users/$BUILTIN_ADMIN_ID/disable" "$ADMIN_TOKEN")" = 409 ] || fail "the sole enabled administrator could be disabled"
[ "$(status_of PUT "/api/admin/users/$BUILTIN_ADMIN_ID/roles" "$ADMIN_TOKEN" '{"roles":[]}')" = 409 ] || fail "the sole enabled administrator lost its role"
[ "$(status_of GET "/api/admin/users/$BUILTIN_ADMIN_ID" "$ADMIN_TOKEN")" = 200 ] || fail "administrator could not be read after the refusals"
[ "$(jq -r '.enabled' "$BODY_FILE")" = true ] || fail "the administrator is no longer enabled"
[ "$(jq -r '.roles | join(",")' "$BODY_FILE")" = Administrator ] || fail "the administrator lost the Administrator role"
pass "the sole enabled administrator cannot be disabled or lose its role (409, unchanged)"

# --- Persistence across a restart ---------------------------------------------------------
[ "$(status_of POST "/api/admin/users/$USER_ID/disable" "$ADMIN_TOKEN")" = 200 ] || fail "could not disable the user before the restart"
docker compose restart auth-api >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api was not ready after the restart"
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[ "$(status_of GET /api/admin/users "$ADMIN_TOKEN")" = 200 ] || fail "administrator could not list users after the restart"
[ "$(jq length "$BODY_FILE")" = 2 ] || fail "the restart changed the number of users"
[ "$(jq -r --arg id "$USER_ID" '.[] | select(.id == $id) | "\(.email) \(.enabled)"' "$BODY_FILE")" = "operator@example.test false" ] \
  || fail "the created user, its email, or its disabled state did not persist"
[ "$(jq -r --arg id "$BUILTIN_ADMIN_ID" '[.[] | select(.id == $id)] | length' "$BODY_FILE")" = 1 ] || fail "the built-in administrator was re-created or lost"
pass "restart: users, emails, and enabled state persisted; the administrator was not re-created"

# --- No secret reaches the auth-api logs (NFR-002) ----------------------------------------
LOGS="$(docker compose logs auth-api 2>&1)"
for secret in "$USER_PASSWORD" "Not-The-Password1" "$ADMIN_TOKEN" "$OPERATOR_TOKEN" "PRIVATE KEY" '"password"'; do
  if grep -qF -- "$secret" <<<"$LOGS"; then fail "a secret or credential appears in the auth-api logs"; fi
done
pass "auth-api logs contain no passwords, access tokens, or private key material"

docker compose down -v >/dev/null

# --- Regression: Phase 2 acceptance (which runs Phase 1), with this script's variables unset ----
(
  unset AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH \
        AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE
  "$REPO_ROOT/tests/acceptance/phase-2.sh"
) || fail "Phase 2 regression failed"
pass "Phase 2 acceptance regression (includes Phase 1)"

echo "Phase 3 acceptance: ALL PASS"
