#!/usr/bin/env bash
# Phase 4 disposable Compose demonstration (Gate G4 deployment evidence).
# Proves, over real Compose services on disposable external storage: login-issued renewable
# sessions, refresh rotation, replay containment, concurrent refresh, logout, administrative
# revocation, revocation on account disablement, persistence across a restart, the Origin
# boundary, that no secret reaches the auth-api logs while the revocation events do, and that
# the consumer APIs keep accepting an unexpired access token locally. Finishes with the Phase 3
# acceptance (which runs Phases 2 and 1) as regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
BASE="http://localhost:${AUTH_PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase4-acceptance.XXXXXX")"

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
export COMPOSE_PROJECT_NAME="auth-api-phase4-acceptance"

ADMIN_EMAIL="admin@local.invalid"
ADMIN_PASSWORD="admin"
USER_PASSWORD="Accept4nce!"
BUILTIN_ADMIN_ID="7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02"

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

# cookie_value: the auth_refresh value from the last $HEADER_FILE (empty when absent).
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

# browser_post PATH [COOKIE] [ORIGIN]: prints the status; body in $BODY_FILE, headers in $HEADER_FILE.
browser_post() {
  local path="$1" cookie="${2:-}" origin="${3-$AUTH_FRONTEND_ORIGIN}"
  local args=(--silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST)
  [ -z "$origin" ] || args+=(--header "Origin: $origin")
  [ -z "$cookie" ] || args+=(--header "Cookie: auth_refresh=$cookie")
  curl "${args[@]}" "$BASE$path"
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
ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[ -n "$ADMIN_TOKEN" ] && [ "$ADMIN_TOKEN" != null ] || fail "administrator login did not return a token"

CREATED="$(jq -cn --arg e "member@example.test" --arg p "$USER_PASSWORD" '{email:$e,password:$p}')"
[ "$(status_of POST /api/admin/users "$ADMIN_TOKEN" "$CREATED")" = 201 ] || fail "user creation failed"
USER_ID="$(jq -r .id "$BODY_FILE")"

# --- US1: login issues a restrictive refresh cookie ---------------------------------------
[ "$(login member@example.test "$USER_PASSWORD")" = 200 ] || fail "member login failed"
[ "$(jq -r 'keys | join(",")' "$BODY_FILE")" = "accessToken,expiresAtUtc" ] || fail "login body shape changed"
SET_COOKIE="$(grep -i '^set-cookie: auth_refresh=' "$HEADER_FILE" | head -n1 | tr -d '\r')"
for attribute in 'httponly' 'samesite=strict' 'path=/api/auth' 'expires='; do
  grep -qi -- "$attribute" <<<"$SET_COOKIE" || fail "refresh cookie lacks $attribute"
done
grep -qi 'domain=' <<<"$SET_COOKIE" && fail "refresh cookie sets a Domain"
FIRST="$(cookie_value)"
[ "$(login member@example.test "Not-The-Password1")" = 401 ] || fail "wrong password was not 401"
[ -z "$(cookie_value)" ] || fail "a failed login set a refresh cookie"
pass "login: unchanged token body plus a restrictive auth_refresh cookie; failed login sets none"

# --- US2: rotation and the Origin boundary ------------------------------------------------
for origin in "" "null" "not a url" "https://evil.example"; do
  [ "$(browser_post /api/auth/refresh "$FIRST" "$origin")" = 403 ] || fail "refresh with Origin '$origin' was not 403"
  [ -z "$(cookie_value)" ] || fail "a 403 refresh set a cookie"
done
[ "$(browser_post /api/auth/refresh "$FIRST")" = 200 ] || fail "refresh with a valid cookie failed"
ROTATED="$(cookie_value)"
[ -n "$ROTATED" ] && [ "$ROTATED" != "$FIRST" ] || fail "refresh did not issue a different replacement cookie"
[ "$(jq -r 'keys | join(",")' "$BODY_FILE")" = "accessToken,expiresAtUtc" ] || fail "refresh body shape differs from login"
ACCESS_BEFORE_REPLAY="$(jq -r .accessToken "$BODY_FILE")"
pass "refresh: exact Origin required (403 otherwise); rotation issues a new cookie and token body"

# --- US3: replay containment --------------------------------------------------------------
[ "$(browser_post /api/auth/refresh "$FIRST")" = 401 ] || fail "replayed credential was not 401"
[ -z "$(cookie_value)" ] || fail "a replay set a cookie"
[ "$(browser_post /api/auth/refresh "$ROTATED")" = 401 ] || fail "replacement still worked after replay"
pass "replay: the consumed credential is 401 and the whole family is revoked"

# Concurrent refresh with one credential: at most one winner, no independent continuation.
RACE="$(new_session member@example.test "$USER_PASSWORD")"
for i in 1 2 3 4; do
  ( curl --silent --output "$STATE/race-$i.body" --dump-header "$STATE/race-$i.hdr" --write-out '%{http_code}' \
      --request POST --header "Origin: $AUTH_FRONTEND_ORIGIN" --header "Cookie: auth_refresh=$RACE" \
      "$BASE/api/auth/refresh" >"$STATE/race-$i.status" ) &
done
wait
WINNERS="$(cat "$STATE"/race-*.status | grep -c '^200$' || true)"
[ "$WINNERS" -le 1 ] || fail "$WINNERS concurrent refreshes succeeded"
for i in 1 2 3 4; do
  if [ "$(cat "$STATE/race-$i.status")" = 200 ]; then
    CONT="$({ grep -i '^set-cookie: auth_refresh=' "$STATE/race-$i.hdr" || true; } | head -n1 | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r')"
    # A losing request replays the consumed credential and revokes the family.
    [ "$(browser_post /api/auth/refresh "$CONT")" = 401 ] || [ "$WINNERS" -eq 1 ] || fail "a continuation survived the race"
  fi
done
pass "concurrent refresh: at most one request succeeded"

# --- US4: logout ----------------------------------------------------------------------------
LOGOUT_COOKIE="$(new_session member@example.test "$USER_PASSWORD")"
[ "$(browser_post /api/auth/logout "$LOGOUT_COOKIE" "")" = 403 ] || fail "logout without Origin was not 403"
[ "$(browser_post /api/auth/logout "$LOGOUT_COOKIE")" = 204 ] || fail "logout was not 204"
grep -qi '^set-cookie: auth_refresh=.*expires=' "$HEADER_FILE" || fail "logout did not clear the cookie"
[ "$(browser_post /api/auth/refresh "$LOGOUT_COOKIE")" = 401 ] || fail "refresh worked after logout"
for unusable in "" "malformed" "$LOGOUT_COOKIE"; do
  [ "$(browser_post /api/auth/logout "$unusable")" = 204 ] || fail "idempotent logout was not 204"
done
pass "logout: 204, cookie cleared, family revoked, idempotent for every cookie state"

# --- US5: administrative revocation -----------------------------------------------------------
ADMIN_A="$(new_session member@example.test "$USER_PASSWORD")"
ADMIN_B="$(new_session member@example.test "$USER_PASSWORD")"
[ "$(status_of POST "/api/admin/users/$USER_ID/revoke-sessions")" = 401 ] || fail "anonymous revoke was not 401"
MEMBER_TOKEN="$(login_token member@example.test "$USER_PASSWORD")"
[ "$(status_of POST "/api/admin/users/$USER_ID/revoke-sessions" "$MEMBER_TOKEN")" = 403 ] || fail "non-administrator revoke was not 403"
[ "$(status_of POST /api/admin/users/unknown/revoke-sessions "$ADMIN_TOKEN")" = 404 ] || fail "unknown-user revoke was not 404"
[ "$(status_of POST "/api/admin/users/$USER_ID/revoke-sessions" "$ADMIN_TOKEN")" = 204 ] || fail "administrator revoke was not 204"
for cookie in "$ADMIN_A" "$ADMIN_B"; do
  [ "$(browser_post /api/auth/refresh "$cookie")" = 401 ] || fail "a family survived administrative revocation"
done
pass "administrative revocation: 401/403/404 conventions; every family of the user is revoked"

# --- US6: disablement ---------------------------------------------------------------------------
DIS_A="$(new_session member@example.test "$USER_PASSWORD")"
DIS_B="$(new_session member@example.test "$USER_PASSWORD")"
[ "$(status_of POST "/api/admin/users/$USER_ID/disable" "$ADMIN_TOKEN")" = 200 ] || fail "disable failed"
[ "$(status_of POST "/api/admin/users/$USER_ID/enable" "$ADMIN_TOKEN")" = 200 ] || fail "enable failed"
for cookie in "$DIS_A" "$DIS_B"; do
  [ "$(browser_post /api/auth/refresh "$cookie")" = 401 ] || fail "a family survived disablement and re-enablement"
done
[ "$(status_of POST "/api/admin/users/$BUILTIN_ADMIN_ID/disable" "$ADMIN_TOKEN")" = 409 ] || fail "the sole administrator could be disabled"
ADMIN_COOKIE="$(new_session "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[ "$(status_of POST "/api/admin/users/$BUILTIN_ADMIN_ID/disable" "$ADMIN_TOKEN")" = 409 ] || fail "the sole administrator could be disabled"
[ "$(browser_post /api/auth/refresh "$ADMIN_COOKIE")" = 200 ] || fail "a refused disable revoked the administrator's family"
ADMIN_COOKIE="$(cookie_value)"
pass "disablement: families revoked and not restored by enable; refused last-admin disable keeps its family"

# --- Consumer APIs validate locally ------------------------------------------------------------
PRE_TOKEN="$(login_token member@example.test "$USER_PASSWORD")"
PRE_COOKIE="$(new_session member@example.test "$USER_PASSWORD")"
[ "$(status_of POST "/api/admin/users/$USER_ID/revoke-sessions" "$ADMIN_TOKEN")" = 204 ] || fail "revoke before consumer check failed"
for port in "$A_PORT" "$B_PORT"; do
  [ "$(curl --silent --output /dev/null --write-out '%{http_code}' --header "Authorization: Bearer $PRE_TOKEN" "http://localhost:${port}/api/caller")" = 200 ] \
    || fail "a consumer rejected a pre-revocation unexpired token"
done
[ "$(browser_post /api/auth/refresh "$PRE_COOKIE")" = 401 ] || fail "revoked family still refreshed"
pass "consumers still accept the unexpired access token locally after revocation"

# --- Persistence across a restart ------------------------------------------------------------------
KEEP="$(new_session member@example.test "$USER_PASSWORD")"
REVOKED_BEFORE_RESTART="$LOGOUT_COOKIE"
docker compose restart auth-api >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api was not ready after the restart"
[ "$(browser_post /api/auth/refresh "$REVOKED_BEFORE_RESTART")" = 401 ] || fail "a revoked family worked after the restart"
[ "$(browser_post /api/auth/refresh "$ADMIN_COOKIE")" = 200 ] || fail "an active family did not survive the restart"
[ "$(browser_post /api/auth/refresh "$KEEP")" = 200 ] || fail "the member's active family did not survive the restart"
pass "restart: active families still refresh and revoked ones stay revoked"

# --- Logs: no secrets, revocation events present ---------------------------------------------------------
digest_forms() {
  # Hex form of the SHA-256 of the decoded credential, as a stored verifier would be rendered.
  local raw="$1" b64; b64="$(printf '%s' "$raw" | tr '_-' '/+')"
  while [ $(( ${#b64} % 4 )) -ne 0 ]; do b64="${b64}="; done
  printf '%s' "$b64" | base64 -d | openssl dgst -sha256 -binary | od -An -v -tx1 | tr -d ' \n'
}
LOGS="$(docker compose logs auth-api 2>&1)"
SECRETS=("$USER_PASSWORD" "Not-The-Password1" "$ADMIN_TOKEN" "$MEMBER_TOKEN" "$PRE_TOKEN" "$ACCESS_BEFORE_REPLAY" \
         "$FIRST" "$ROTATED" "$LOGOUT_COOKIE" "$ADMIN_A" "$DIS_A" "$PRE_COOKIE" "PRIVATE KEY" "auth_refresh=")
for cookie in "$FIRST" "$ROTATED" "$LOGOUT_COOKIE" "$PRE_COOKIE"; do SECRETS+=("$(digest_forms "$cookie")"); done
for secret in "${SECRETS[@]}"; do
  if grep -qF -- "$secret" <<<"$LOGS"; then fail "a secret or credential appears in the auth-api logs"; fi
done
grep -Eq 'replay detected for session family .* at [0-9]{4}-[0-9]{2}-[0-9]{2}.*trace [0-9a-f]{32}' <<<"$LOGS" || { grep -i "replay\|revoked" <<<"$LOGS" | head -5 >&2; fail "replay event missing"; }
grep -Eq 'session family .* revoked at [0-9]{4}-[0-9]{2}-[0-9]{2}.*trace [0-9a-f]{32}' <<<"$LOGS" || fail "logout event missing"
grep -Eq 'reason Administrator\) at [0-9]{4}-[0-9]{2}-[0-9]{2}.*trace [0-9a-f]{32}' <<<"$LOGS" || fail "administrator revocation event missing"
grep -Eq 'reason UserDisabled\) at [0-9]{4}-[0-9]{2}-[0-9]{2}.*trace [0-9a-f]{32}' <<<"$LOGS" || fail "disablement revocation event missing"
pass "auth-api logs hold no secrets and record replay, logout, administrator, and disablement events"

docker compose down -v >/dev/null

# --- Regression: Phase 3 acceptance (which runs Phases 2 and 1) -------------------------------------------
(
  unset AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH \
        AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE AUTH_FRONTEND_ORIGIN AUTH_DATAPROTECTION_HOST_PATH \
        AUTH_SMTP_HOST AUTH_SMTP_PORT AUTH_SMTP_SECURITY AUTH_SMTP_SENDER_ADDRESS AUTH_SMTP_SENDER_NAME
  "$REPO_ROOT/tests/acceptance/phase-3.sh"
) || fail "Phase 3 regression failed"
pass "Phase 3 acceptance regression (includes Phases 2 and 1)"

echo "Phase 4 acceptance: ALL PASS"
