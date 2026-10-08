#!/usr/bin/env bash
# Phase 5 disposable Compose demonstration (Gate G5 deployment evidence).
# Proves, over real Compose services on disposable external storage: authenticated password
# change (rejections and success), retirement of the initial administrator password, revocation
# of the user's other renewable sessions while the session whose cookie accompanied the change
# keeps renewing, persistence of the new secret across a restart, and that no password, token,
# cookie, or hash reaches the auth-api logs while the change event does. Finishes with the
# Phase 4 acceptance (which runs Phases 3, 2 and 1) as regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
BASE="http://localhost:${AUTH_PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase5-acceptance.XXXXXX")"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$STATE/keys/jwt-public.pem"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_JWT_CLOCK_SKEW_SECONDS="30"
export AUTH_FRONTEND_ORIGIN="https://frontend.acceptance"
# Phase 6 settings (required by compose.yml): a disposable key-ring directory and a dummy SMTP sender
# that these scripts never use.
export AUTH_DATAPROTECTION_HOST_PATH="$STATE/dataprotection"
export AUTH_SMTP_HOST="${AUTH_SMTP_HOST:-smtp.acceptance.invalid}"
export AUTH_SMTP_PORT="${AUTH_SMTP_PORT:-2525}"
export AUTH_SMTP_SECURITY="${AUTH_SMTP_SECURITY:-None}"
export AUTH_SMTP_SENDER_ADDRESS="${AUTH_SMTP_SENDER_ADDRESS:-no-reply@acceptance.invalid}"
export AUTH_SMTP_SENDER_NAME="${AUTH_SMTP_SENDER_NAME:-Authentication API Acceptance}"
export AUTH_HTTP_PORT="$AUTH_PORT"
export API_A_HTTP_PORT="$A_PORT"
export API_B_HTTP_PORT="$B_PORT"
export COMPOSE_PROJECT_NAME="auth-api-phase5-acceptance"

ADMIN_EMAIL="admin@local.invalid"
INITIAL_PASSWORD="admin"
ADMIN_NEW_PASSWORD="Retired-Admin-1!"
USER_PASSWORD="Accept4nce!"
USER_NEW_PASSWORD="Fresh-Secret-9!"

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

BODY_FILE="$STATE/body.out"
HEADER_FILE="$STATE/headers.out"

# status_of METHOD PATH [TOKEN] [JSON]: prints the status; the body goes to $BODY_FILE.
status_of() {
  local method="$1" path="$2" token="${3:-}" json="${4:-}"
  local args=(--silent --output "$BODY_FILE" --write-out '%{http_code}' --request "$method")
  [ -z "$token" ] || args+=(--header "Authorization: Bearer $token")
  [ -z "$json" ] || args+=(--header 'Content-Type: application/json' --data "$json")
  curl "${args[@]}" "$BASE$path"
}

# login EMAIL PASSWORD: prints the status; body in $BODY_FILE, headers in $HEADER_FILE.
login() {
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' \
    --request POST "$BASE/api/auth/login" --header 'Content-Type: application/json' \
    --data "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
}

cookie_value() {
  { grep -i '^set-cookie: auth_refresh=' "$HEADER_FILE" || true; } | head -n1 | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r'
}

login_token() { login "$1" "$2" >/dev/null; jq -r .accessToken "$BODY_FILE"; }

# new_session EMAIL PASSWORD: logs in and prints the refresh cookie value.
new_session() {
  [ "$(login "$1" "$2")" = 200 ] || fail "login for $1 failed"
  local value; value="$(cookie_value)"
  [ -n "$value" ] || fail "login for $1 returned no refresh cookie"
  printf '%s' "$value"
}

# refresh COOKIE: prints the status; headers in $HEADER_FILE.
refresh() {
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST \
    --header "Origin: $AUTH_FRONTEND_ORIGIN" --header "Cookie: auth_refresh=$1" "$BASE/api/auth/refresh"
}

# change_password TOKEN CURRENT NEW [COOKIE]: prints the status; headers in $HEADER_FILE.
change_password() {
  local token="$1" current="$2" new="$3" cookie="${4:-}"
  local args=(--silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST
              --header 'Content-Type: application/json'
              --data "$(jq -cn --arg c "$current" --arg n "$new" '{currentPassword:$c,newPassword:$n}')")
  [ -z "$token" ] || args+=(--header "Authorization: Bearer $token")
  [ -z "$cookie" ] || args+=(--header "Cookie: auth_refresh=$cookie")
  curl "${args[@]}" "$BASE/api/auth/change-password"
}

# --- Disposable external storage ---------------------------------------------------------
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
install -d -m 0777 "$STATE/dataprotection"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"

docker compose up --build -d >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api did not become ready"

# --- US1: rejections leave everything unchanged ---------------------------------------------------
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$INITIAL_PASSWORD")"
[ -n "$ADMIN_TOKEN" ] && [ "$ADMIN_TOKEN" != null ] || fail "initial administrator login failed"

[ "$(change_password "" "$INITIAL_PASSWORD" "$ADMIN_NEW_PASSWORD")" = 401 ] || fail "unauthenticated change was not 401"
grep -qi '^www-authenticate: bearer' "$HEADER_FILE" || fail "unauthenticated change lacks the Bearer challenge"
[ "$(change_password "$ADMIN_TOKEN" "wrong-current" "$ADMIN_NEW_PASSWORD")" = 401 ] || fail "wrong current password was not 401"
grep -qi '^www-authenticate' "$HEADER_FILE" && fail "wrong current password carried a Bearer challenge"
[ "$(change_password "$ADMIN_TOKEN" "$INITIAL_PASSWORD" "abc")" = 400 ] || fail "policy-violating password was not 400"
[ "$(login "$ADMIN_EMAIL" "$INITIAL_PASSWORD")" = 200 ] || fail "a rejected change altered the password"
pass "rejections: no token 401, wrong current 401, policy violation 400; password unchanged"

# --- US3 + US2: retire the initial password; other sessions end; the current one survives -----------
KEEP="$(new_session "$ADMIN_EMAIL" "$INITIAL_PASSWORD")"
OTHER="$(new_session "$ADMIN_EMAIL" "$INITIAL_PASSWORD")"
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$INITIAL_PASSWORD")"
[ "$(change_password "$ADMIN_TOKEN" "$INITIAL_PASSWORD" "$ADMIN_NEW_PASSWORD" "$KEEP")" = 204 ] || fail "administrator password change was not 204"
[ -s "$BODY_FILE" ] && fail "the change response had a body"
grep -qi '^set-cookie:' "$HEADER_FILE" && fail "the change response set a cookie"
[ "$(login "$ADMIN_EMAIL" "$INITIAL_PASSWORD")" = 401 ] || fail "the initial password still authenticates"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_NEW_PASSWORD")" = 200 ] || fail "the new administrator password does not authenticate"
pass "initial administrator password replaced without email; the old one no longer authenticates"

[ "$(refresh "$OTHER")" = 401 ] || fail "another session still refreshes after the change"
[ "$(refresh "$KEEP")" = 200 ] || fail "the session that made the change cannot refresh"
KEEP="$(cookie_value)"
pass "other renewable sessions are revoked; the session whose cookie accompanied the change keeps renewing"

# Consumers keep validating an access token issued before the change, locally.
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' --header "Authorization: Bearer $ADMIN_TOKEN" "http://localhost:${A_PORT}/api/caller")" = 200 ] \
  || fail "api-a rejected a token issued before the change"
pass "an access token issued before the change is still accepted by the consumers"

# --- An ordinary user, and the no-cookie rule -------------------------------------------------------------
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$ADMIN_NEW_PASSWORD")"
CREATED="$(jq -cn --arg e "member@example.test" --arg p "$USER_PASSWORD" '{email:$e,password:$p}')"
[ "$(status_of POST /api/admin/users "$ADMIN_TOKEN" "$CREATED")" = 201 ] || fail "user creation failed"
USER_A="$(new_session member@example.test "$USER_PASSWORD")"
USER_B="$(new_session member@example.test "$USER_PASSWORD")"
USER_TOKEN="$(login_token member@example.test "$USER_PASSWORD")"
[ "$(change_password "$USER_TOKEN" "$USER_PASSWORD" "$USER_NEW_PASSWORD")" = 204 ] || fail "ordinary user change was not 204"
[ "$(login member@example.test "$USER_PASSWORD")" = 401 ] || fail "the user's old password still authenticates"
[ "$(login member@example.test "$USER_NEW_PASSWORD")" = 200 ] || fail "the user's new password does not authenticate"
for cookie in "$USER_A" "$USER_B"; do
  [ "$(refresh "$cookie")" = 401 ] || fail "a session survived a change made without a cookie"
done
pass "ordinary user changes the password; without a cookie every session of the user is revoked"

# --- Persistence across a restart ----------------------------------------------------------------------------
docker compose restart auth-api >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api was not ready after the restart"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_NEW_PASSWORD")" = 200 ] || fail "the new administrator password was lost on restart"
[ "$(login "$ADMIN_EMAIL" "$INITIAL_PASSWORD")" = 401 ] || fail "the initial password was restored on restart"
[ "$(refresh "$KEEP")" = 200 ] || fail "the kept session did not survive the restart"
pass "restart: the new administrator secret persists, admin is not restored, the kept session still renews"

# --- Logs: no secret, change event present ---------------------------------------------------------------------------
digest_forms() {
  local raw="$1" b64; b64="$(printf '%s' "$raw" | tr '_-' '/+')"
  while [ $(( ${#b64} % 4 )) -ne 0 ]; do b64="${b64}="; done
  printf '%s' "$b64" | base64 -d | openssl dgst -sha256 -binary | od -An -v -tx1 | tr -d ' \n'
}
LOGS="$(docker compose logs auth-api 2>&1)"
SECRETS=('"currentPassword"' "$ADMIN_NEW_PASSWORD" "$USER_PASSWORD" "$USER_NEW_PASSWORD" "wrong-current" \
         "$ADMIN_TOKEN" "$USER_TOKEN" "$KEEP" "$OTHER" "$USER_A" "PRIVATE KEY" "auth_refresh=")
for cookie in "$OTHER" "$USER_A"; do SECRETS+=("$(digest_forms "$cookie")"); done
for secret in "${SECRETS[@]}"; do
  if grep -qF -- "$secret" <<<"$LOGS"; then fail "a secret or credential appears in the auth-api logs"; fi
done
grep -Eq 'Password changed for user .* other renewable session families revoked, current session kept: True, at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}' <<<"$LOGS" \
  || fail "the password-change event (with the kept session) is missing"
grep -Eq 'Password changed for user .* current session kept: False, at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}' <<<"$LOGS" \
  || fail "the password-change event (without a kept session) is missing"
pass "auth-api logs hold no passwords, tokens, cookies, or hashes, and record the change events with UTC time and trace"

docker compose down -v >/dev/null

# --- Regression: Phase 4 acceptance (which runs Phases 3, 2 and 1) --------------------------------------------------------
(
  unset AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH \
        AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE AUTH_FRONTEND_ORIGIN AUTH_DATAPROTECTION_HOST_PATH \
        AUTH_SMTP_HOST AUTH_SMTP_PORT AUTH_SMTP_SECURITY AUTH_SMTP_SENDER_ADDRESS AUTH_SMTP_SENDER_NAME
  "$REPO_ROOT/tests/acceptance/phase-4.sh"
) || fail "Phase 4 regression failed"
pass "Phase 4 acceptance regression (includes Phases 3, 2 and 1)"

echo "Phase 5 acceptance: ALL PASS"
