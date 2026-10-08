#!/usr/bin/env bash
# Phase 6 disposable Compose demonstration (Gate G6 deployment evidence).
# Proves, over real Compose services on disposable external storage and a disposable SMTP sink:
# anti-enumeration of forgot-password, delivery of a reset token through SMTP, that the token
# survives `restart`, `up --force-recreate` and `down -v` + `up` because the Data Protection key
# ring is a host bind mount (not a Compose volume, mode 0700), reset-password with revocation of
# every renewable session, single use of the token, and that no token, password, SMTP secret,
# cookie, or hash reaches the auth-api logs while the recovery events do. Finishes with the Phase 5
# acceptance (which runs Phases 4, 3, 2 and 1) as regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
SINK_PORT="${MAIL_SINK_HTTP_PORT:-18025}"
BASE="http://localhost:${AUTH_PORT}"
SINK="http://localhost:${SINK_PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase6-acceptance.XXXXXX")"
RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:10.0"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_DATAPROTECTION_HOST_PATH="$STATE/dataprotection"
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$STATE/keys/jwt-public.pem"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_JWT_CLOCK_SKEW_SECONDS="30"
export AUTH_FRONTEND_ORIGIN="https://frontend.acceptance"
export AUTH_SMTP_HOST="mail-sink"
export AUTH_SMTP_PORT="1025"
export AUTH_SMTP_SECURITY="None"
export AUTH_SMTP_USERNAME="smtp-user"
export AUTH_SMTP_PASSWORD="smtp-secret-pw"
export AUTH_SMTP_SENDER_ADDRESS="no-reply@acceptance.invalid"
export AUTH_SMTP_SENDER_NAME="Authentication API Acceptance"
# Phase 7 limits are lifted for regression runs; phase-7.sh sets its own small values.
export AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT="${AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT="${AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT="${AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT="${AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT:-100000}"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT="${AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT:-100000}"
export AUTH_HTTP_PORT="$AUTH_PORT"
export API_A_HTTP_PORT="$A_PORT"
export API_B_HTTP_PORT="$B_PORT"
export MAIL_SINK_HTTP_PORT="$SINK_PORT"
export COMPOSE_PROJECT_NAME="auth-api-phase6-acceptance"
# The sink exists only in this override; compose.yml itself gains no service.
export COMPOSE_FILE="compose.yml:tests/acceptance/compose.mail-sink.yml"

ADMIN_EMAIL="admin@local.invalid"
ADMIN_PASSWORD="admin"
USER_EMAIL="member@example.test"
USER_PASSWORD="Accept4nce!"
USER_NEW_PASSWORD="Fresh-Secret-9!"

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; exit 1; }

cleanup() {
  docker compose down -v >/dev/null 2>&1 || true
  # The key ring is owned by the container user with mode 0700; remove it as that user's namespace root.
  docker run --rm --user root --entrypoint sh -v "$STATE:/state" "$RUNTIME_IMAGE" -c 'rm -rf /state/dataprotection' >/dev/null 2>&1 || true
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

status_of() {
  local method="$1" path="$2" token="${3:-}" json="${4:-}"
  local args=(--silent --output "$BODY_FILE" --write-out '%{http_code}' --request "$method")
  [ -z "$token" ] || args+=(--header "Authorization: Bearer $token")
  [ -z "$json" ] || args+=(--header 'Content-Type: application/json' --data "$json")
  curl "${args[@]}" "$BASE$path"
}

login() {
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' \
    --request POST "$BASE/api/auth/login" --header 'Content-Type: application/json' \
    --data "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
}

cookie_value() {
  { grep -i '^set-cookie: auth_refresh=' "$HEADER_FILE" || true; } | head -n1 | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r'
}

login_token() { login "$1" "$2" >/dev/null; jq -r .accessToken "$BODY_FILE"; }

new_session() {
  [ "$(login "$1" "$2")" = 200 ] || fail "login for $1 failed"
  local value; value="$(cookie_value)"
  [ -n "$value" ] || fail "login for $1 returned no refresh cookie"
  printf '%s' "$value"
}

refresh() {
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST \
    --header "Origin: $AUTH_FRONTEND_ORIGIN" --header "Cookie: auth_refresh=$1" "$BASE/api/auth/refresh"
}

# forgot EMAIL: prints "status|headers-without-Date|body" so two responses can be compared exactly.
forgot() {
  local status
  status="$(curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST \
    --header 'Content-Type: application/json' --data "$(jq -cn --arg e "$1" '{email:$e}')" "$BASE/api/auth/forgot-password")"
  printf '%s|%s|%s' "$status" "$(grep -v -i -e '^date:' -e '^HTTP/' "$HEADER_FILE" | tr -d '\r' | sort | tr '\n' ';')" "$(cat "$BODY_FILE")"
}

reset_password() {
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST \
    --header 'Content-Type: application/json' \
    --data "$(jq -cn --arg e "$1" --arg t "$2" --arg p "$3" '{email:$e,token:$t,newPassword:$p}')" "$BASE/api/auth/reset-password"
}

# Container logs vanish when the container is recreated, so they are collected before each such step.
LOG_FILE="$STATE/auth-api.log"
collect_logs() { docker compose logs --no-color auth-api >>"$LOG_FILE" 2>&1 || true; }

up_and_wait() {
  docker compose up -d "$@" >/dev/null
  wait_for "$BASE/health/ready" || fail "auth-api did not become ready"
}

# --- Disposable external storage; the key ring is owned by the container user, mode 0700 ---------------
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
install -d -m 0777 "$STATE/dataprotection"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"

docker compose build >/dev/null
APP_UID_VALUE="$(docker run --rm --entrypoint sh "$RUNTIME_IMAGE" -c 'printf %s "$APP_UID"')"
docker run --rm --user root --entrypoint sh -v "$STATE:/state" "$RUNTIME_IMAGE" \
  -c "chown $APP_UID_VALUE:$APP_UID_VALUE /state/dataprotection && chmod 0700 /state/dataprotection"

up_and_wait
wait_for "$SINK/api/v1/info" || fail "the mail sink did not become ready"

ADMIN_TOKEN="$(login_token "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
CREATED="$(jq -cn --arg e "$USER_EMAIL" --arg p "$USER_PASSWORD" '{email:$e,password:$p}')"
[ "$(status_of POST /api/admin/users "$ADMIN_TOKEN" "$CREATED")" = 201 ] || fail "user creation failed"

# --- US1: anti-enumeration and delivery -----------------------------------------------------------
SESSION_A="$(new_session "$USER_EMAIL" "$USER_PASSWORD")"
SESSION_B="$(new_session "$USER_EMAIL" "$USER_PASSWORD")"

KNOWN="$(forgot "$USER_EMAIL")"
UNKNOWN="$(forgot "nobody@example.test")"
[ "$KNOWN" = "$UNKNOWN" ] || fail "forgot-password differs between an existing and an unknown account"
case "$KNOWN" in 204\|*) ;; *) fail "forgot-password was not 204: $KNOWN" ;; esac
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' --request POST --header 'Content-Type: application/json' \
      --data '{"email":"not-an-email"}' "$BASE/api/auth/forgot-password")" = 400 ] || fail "an invalid forgot-password body was not 400"
pass "forgot-password answers the identical 204 for an existing and an unknown address; invalid input is 400"

for _ in $(seq 1 20); do
  [ "$(curl --silent "$SINK/api/v1/messages" | jq '.messages | length')" -ge 1 ] && break
  sleep 1
done
[ "$(curl --silent "$SINK/api/v1/messages" | jq '.messages | length')" = 1 ] || fail "expected exactly one recovery email (existing account only)"
MESSAGE_ID="$(curl --silent "$SINK/api/v1/messages" | jq -r '.messages[0].ID')"
[ "$(curl --silent "$SINK/api/v1/message/$MESSAGE_ID" | jq -r '.To[0].Address')" = "$USER_EMAIL" ] || fail "the email went to the wrong address"
TOKEN="$(curl --silent "$SINK/api/v1/message/$MESSAGE_ID" | jq -r '.Text' | grep '^Token:' | head -n1 | sed 's/^Token:[[:space:]]*//' | tr -d '\r')"
[ -n "$TOKEN" ] || fail "the recovery email carries no token"
pass "one email was delivered over SMTP to the existing account only, carrying the reset token"

# --- US4: the token survives restart, recreation, and down -v ----------------------------------------------
docker compose restart auth-api >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api was not ready after restart"
pass "restart: auth-api ready again"

collect_logs
up_and_wait --force-recreate auth-api
pass "up --force-recreate: container recreated"

collect_logs
docker compose down -v >/dev/null
up_and_wait
pass "down -v + up on the same host directories: stack back"

# The key ring is a host bind mount, not a Compose volume, owned by the container user with mode 0700.
MOUNTS="$(docker inspect "$(docker compose ps -q auth-api)" --format '{{json .Mounts}}')"
[ "$(jq -r --arg src "$AUTH_DATAPROTECTION_HOST_PATH" '[.[] | select(.Destination == "/var/lib/auth-api/dataprotection" and .Type == "bind" and .Source == $src)] | length' <<<"$MOUNTS")" = 1 ] \
  || fail "the key ring is not the expected host bind mount"
[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT_NAME")" ] || fail "a Compose-managed volume exists"
[ "$(stat -c %a "$AUTH_DATAPROTECTION_HOST_PATH")" = 700 ] || fail "the key ring directory is not mode 0700"
[ "$(stat -c %u "$AUTH_DATAPROTECTION_HOST_PATH")" = "$APP_UID_VALUE" ] || fail "the key ring directory is not owned by the container user"
# The directory is private to the container user, so list it as root through a throwaway container.
docker run --rm --user root --entrypoint sh -v "$AUTH_DATAPROTECTION_HOST_PATH:/keys:ro" "$RUNTIME_IMAGE" -c 'ls /keys/*.xml >/dev/null 2>&1' \
  || fail "no key file was persisted in the key ring"
pass "the key ring is a host bind mount (no Compose volume), owned by the container user, mode 0700, holding persisted keys"

# --- US2 + US3: reset, revocation of every session, single use -------------------------------------------------
[ "$(reset_password "$USER_EMAIL" "$TOKEN" "$USER_NEW_PASSWORD")" = 204 ] || fail "the token did not reset the password after the lifecycle operations"
pass "the still-valid token reset the password after restart, recreation, and down -v"

[ "$(login "$USER_EMAIL" "$USER_PASSWORD")" = 401 ] || fail "the old password still authenticates"
[ "$(login "$USER_EMAIL" "$USER_NEW_PASSWORD")" = 200 ] || fail "the new password does not authenticate"
for cookie in "$SESSION_A" "$SESSION_B"; do
  [ "$(refresh "$cookie")" = 401 ] || fail "a session survived the reset"
done
[ "$(reset_password "$USER_EMAIL" "$TOKEN" "Another-Secret1!")" = 401 ] || fail "the token was accepted twice"
[ "$(reset_password "$USER_EMAIL" "not-a-token" "Another-Secret1!")" = 401 ] || fail "an invalid token was not 401"
pass "reset: old password rejected, new accepted, every session revoked, token single-use"

# --- Logs: no secret, recovery events present --------------------------------------------------------------------------
collect_logs
LOGS="$(cat "$LOG_FILE")"
SECRETS=("$TOKEN" "$USER_PASSWORD" "$USER_NEW_PASSWORD" "Another-Secret1!" "$AUTH_SMTP_PASSWORD" "$ADMIN_TOKEN" "$SESSION_A" "$SESSION_B" "PRIVATE KEY" "auth_refresh=")
for secret in "${SECRETS[@]}"; do
  if grep -qF -- "$secret" <<<"$LOGS"; then fail "a secret or credential appears in the auth-api logs"; fi
done
grep -Eq 'PasswordResetRequested: reset token issued for user .* at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}' <<<"$LOGS" \
  || fail "the PasswordResetRequested event is missing"
grep -Eq 'PasswordReset: password reset for user .* 2 renewable session families revoked at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}' <<<"$LOGS" \
  || fail "the PasswordReset event is missing"
pass "auth-api logs hold no token, password, SMTP secret, or cookie, and record the recovery events with UTC time and trace"

docker compose down -v >/dev/null

# --- Regression: Phase 5 acceptance (which runs Phases 4, 3, 2 and 1) ----------------------------------------------------------
(
  unset COMPOSE_FILE AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE \
        AUTH_FRONTEND_ORIGIN AUTH_DATAPROTECTION_HOST_PATH MAIL_SINK_HTTP_PORT AUTH_SMTP_HOST AUTH_SMTP_PORT \
        AUTH_SMTP_SECURITY AUTH_SMTP_USERNAME AUTH_SMTP_PASSWORD AUTH_SMTP_SENDER_ADDRESS AUTH_SMTP_SENDER_NAME
  "$REPO_ROOT/tests/acceptance/phase-5.sh"
) || fail "Phase 5 regression failed"
pass "Phase 5 acceptance regression (includes Phases 4, 3, 2 and 1)"

echo "Phase 6 acceptance: ALL PASS"
