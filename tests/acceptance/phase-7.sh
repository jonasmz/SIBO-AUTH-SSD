#!/usr/bin/env bash
# Phase 7 disposable Compose demonstration (Gate G7 deployment evidence).
# Proves, over real Compose services, a disposable reference proxy, and disposable storage: Identity lockout
# after five failures; an application 429 (application/problem+json) for each of the four anonymous policies
# and for the per-address recovery limit, independent of each other and of the account lockout, each with a
# Retry-After no longer than its configured window (no step waits for a window to renew; spec NFR-002, Constitution VI);
# that a forged X-Forwarded-For sent directly is ignored and that
# the reference proxy's overwritten header makes the client (not the proxy, not the forgery) the effective
# origin; the proxy's own first-layer 429; and that no secret reaches the auth-api logs while the lockout,
# login-failure, and rate-limit events do. Finishes with the Phase 6 acceptance (which runs Phases 5-1).
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

AUTH_PORT="${AUTH_HTTP_PORT:-18080}"
A_PORT="${API_A_HTTP_PORT:-18081}"
B_PORT="${API_B_HTTP_PORT:-18082}"
PROXY_PORT="${REFERENCE_PROXY_HTTP_PORT:-18090}"
BASE="http://localhost:${AUTH_PORT}"
PROXY="http://localhost:${PROXY_PORT}"
STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase7-acceptance.XXXXXX")"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_DATAPROTECTION_HOST_PATH="$STATE/dataprotection"
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$STATE/keys/jwt-public.pem"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_JWT_CLOCK_SKEW_SECONDS="30"
export AUTH_FRONTEND_ORIGIN="https://frontend.acceptance"
export AUTH_SMTP_HOST="smtp.acceptance.invalid"
export AUTH_SMTP_PORT="2525"
export AUTH_SMTP_SECURITY="None"
export AUTH_SMTP_USERNAME="smtp-user"
export AUTH_SMTP_PASSWORD="smtp-secret-pw"
export AUTH_SMTP_SENDER_ADDRESS="no-reply@acceptance.invalid"
export AUTH_SMTP_SENDER_NAME="Authentication API Acceptance"
# Small, explicit limits so each policy can be exhausted quickly, with windows long enough that no step crosses one.
export AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT="8"
export AUTH_RATE_LIMIT_LOGIN_WINDOW_SECONDS="300"
export AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT="3"
export AUTH_RATE_LIMIT_REFRESH_WINDOW_SECONDS="60"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT="4"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_WINDOW_SECONDS="60"
export AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT="3"
export AUTH_RATE_LIMIT_RESET_PASSWORD_WINDOW_SECONDS="60"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT="2"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_WINDOW_SECONDS="60"
export AUTH_HTTP_PORT="$AUTH_PORT"
export API_A_HTTP_PORT="$A_PORT"
export API_B_HTTP_PORT="$B_PORT"
export REFERENCE_PROXY_HTTP_PORT="$PROXY_PORT"
export COMPOSE_PROJECT_NAME="auth-api-phase7-acceptance"
# The proxy exists only in this override; compose.yml itself gains no service.
export COMPOSE_FILE="compose.yml:tests/acceptance/compose.reference-proxy.yml"
# Phase 8 topology: this project's own internal network; the frontend gets an address that is not the
# reference proxy's, and only the reference proxy is trusted (compose.reference-proxy.yml).
export AUTH_INTERNAL_SUBNET="172.28.7.0/24"
export FRONTEND_INTERNAL_ADDRESS="172.28.7.20"
# Phase 8: disposable logs, frontend inputs, and the direct-access override for the final topology.
source "$REPO_ROOT/tests/acceptance/deployment-env.sh"

ADMIN_EMAIL="admin@local.invalid"
ADMIN_PASSWORD="admin"
MEMBER_EMAIL="member@example.test"
MEMBER_PASSWORD="Accept4nce!"
WRONG_PASSWORD="Wr0ng-Guess!"
FORGED="203.0.113.99"

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

# post BASE_URL PATH JSON [extra curl args...]: prints the status; body in $BODY_FILE, headers in $HEADER_FILE.
# Every request carries a forged X-Forwarded-For: a client-chosen value that must never become the origin.
post() {
  local url="$1" path="$2" json="$3"; shift 3
  curl --silent --output "$BODY_FILE" --dump-header "$HEADER_FILE" --write-out '%{http_code}' --request POST \
    --header 'Content-Type: application/json' --header "X-Forwarded-For: $FORGED" "$@" --data "$json" "$url$path"
}

login() { post "$BASE" /api/auth/login "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"; }
login_via_proxy() { post "$PROXY" /api/auth/login "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"; }
forgot() { post "$BASE" /api/auth/forgot-password "$(jq -cn --arg e "$1" '{email:$e}')"; }
reset_password() { post "$1" /api/auth/reset-password '{"email":"nobody@example.test","token":"not-a-token","newPassword":"N3w-Secret!"}'; }
refresh() { post "$BASE" /api/auth/refresh '{}' --header "Origin: $AUTH_FRONTEND_ORIGIN" --header "Cookie: auth_refresh=$(printf 'A%.0s' $(seq 1 43))"; }

header_value() { { grep -i "^$1:" "$HEADER_FILE" || true; } | head -n1 | cut -d: -f2- | tr -d '\r' | sed 's/^ *//'; }

# assert_retry_after WINDOW_SECONDS LABEL: the last 429 reports a wait that is positive and within the window.
# The window is asserted as reported; the script never sleeps for it to renew.
assert_retry_after() {
  local seconds
  seconds="$(header_value Retry-After)"
  [[ "$seconds" =~ ^[0-9]+$ ]] && [ "$seconds" -ge 1 ] && [ "$seconds" -le "$1" ] \
    || fail "the $2 429 Retry-After '$seconds' is not within 1..$1 seconds"
}

# --- Disposable external storage ------------------------------------------------------------------------
install -d -m 0755 "$STATE/keys"
install -d -m 0777 "$STATE/data"
install -d -m 0777 "$STATE/dataprotection"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$STATE/keys/jwt-public.pem"
chmod 0644 "$STATE/keys/jwt-private.pem" "$STATE/keys/jwt-public.pem"

docker compose up --build -d >/dev/null
wait_for "$BASE/health/ready" || fail "auth-api did not become ready"
wait_for "$PROXY/health/ready" || fail "the reference proxy did not reach auth-api"

GATEWAY="$(docker network inspect "${COMPOSE_PROJECT_NAME}_default" --format '{{(index .IPAM.Config 0).Gateway}}')"
PROXY_ADDRESS="172.28.7.10"
[ -n "$GATEWAY" ] || fail "could not determine the client address the containers see"

# --- Account lockout (direct to auth-api) ----------------------------------------------------------------
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 200 ] || fail "administrator login failed"
ADMIN_TOKEN="$(jq -r .accessToken "$BODY_FILE")"
CREATED="$(jq -cn --arg e "$MEMBER_EMAIL" --arg p "$MEMBER_PASSWORD" '{email:$e,password:$p}')"
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' --request POST --header 'Content-Type: application/json' \
      --header "Authorization: Bearer $ADMIN_TOKEN" --data "$CREATED" "$BASE/api/admin/users")" = 201 ] || fail "user creation failed"

for _ in 1 2 3 4 5; do
  [ "$(login "$MEMBER_EMAIL" "$WRONG_PASSWORD")" = 401 ] || fail "a wrong password was not 401"
done
[ "$(login "$MEMBER_EMAIL" "$MEMBER_PASSWORD")" = 401 ] || fail "the correct password worked while the account should be locked"
grep -q "Invalid credentials." "$BODY_FILE" || fail "the locked-account response differs from a wrong-password response"
pass "five wrong passwords lock the account: even the correct password is refused with the generic 401"

# --- Application request limits (login policy: 8 per 300 s; 7 requests used so far) --------------------------
# The eighth (last permitted) login names an unknown account: answered like any credential failure.
[ "$(login "nobody@example.test" "$WRONG_PASSWORD")" = 401 ] || fail "the eighth login was not answered normally"
[ "$(login "$MEMBER_EMAIL" "$WRONG_PASSWORD")" = 429 ] || fail "the ninth login was not rate limited"
grep -qi '^content-type: application/problem+json' "$HEADER_FILE" || fail "the login 429 is not application/problem+json"
assert_retry_after "$AUTH_RATE_LIMIT_LOGIN_WINDOW_SECONDS" login
jq -e '.title == "Too Many Requests"' "$BODY_FILE" >/dev/null || fail "the 429 body is not the expected problem details"
grep -q "$MEMBER_EMAIL" "$BODY_FILE" && fail "the 429 body names an account"

# The exhausted login policy does not limit the other policies.
[ "$(refresh)" = 401 ] || fail "refresh was affected by the login limit"
[ "$(forgot "nobody-0@example.test")" = 204 ] || fail "forgot-password was affected by the login limit"
pass "login 429 (problem+json, Retry-After) while the other policies still answer; the lockout stayed in force"

# refresh (3 per 60 s; one used by the probe above)
STATUSES=""
for _ in 1 2 3 4; do STATUSES="$STATUSES $(refresh)"; done
[ "${STATUSES# }" = "401 401 429 429" ] || fail "refresh limit sequence was '${STATUSES# }'"
assert_retry_after "$AUTH_RATE_LIMIT_REFRESH_WINDOW_SECONDS" refresh

# forgot-password: policy 4 per 60 s (one used), address 2 per 60 s (one used for nobody-0)
[ "$(forgot "nobody-0@example.test")" = 204 ] || fail "second request for one address was not answered normally"
[ "$(forgot "NOBODY-0@example.test")" = 429 ] || fail "the per-address recovery limit did not apply across letter case"
jq -e '.title == "Too Many Requests"' "$BODY_FILE" >/dev/null || fail "the address 429 body is not problem details"
assert_retry_after "$AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_WINDOW_SECONDS" forgot-password-address
[ "$(forgot "nobody-1@example.test")" = 204 ] || fail "a different address was limited"
[ "$(forgot "nobody-2@example.test")" = 429 ] || fail "the forgot-password policy did not apply"
assert_retry_after "$AUTH_RATE_LIMIT_FORGOT_PASSWORD_WINDOW_SECONDS" forgot-password
pass "refresh and forgot-password limits (per origin and per address) answer 429 independently"

# --- Trusted proxy boundary -----------------------------------------------------------------------------------
# reset-password through the reference proxy (3 per 60 s): the forged header is overwritten by the proxy.
for expected in 401 401 401 429; do
  [ "$(reset_password "$PROXY")" = "$expected" ] || fail "reset-password through the proxy did not follow 401,401,401,429"
done
grep -qi '^content-type: application/problem+json' "$HEADER_FILE" || fail "the application 429 through the proxy is not problem+json"
assert_retry_after "$AUTH_RATE_LIMIT_RESET_PASSWORD_WINDOW_SECONDS" reset-password
pass "reset-password limit reached through the reference proxy (application 429)"

# The proxy's own first layer (looser: 60 r/min, burst 20) answers with an HTML 429.
PROXY_HTML=0
for _ in $(seq 1 40); do
  CODE="$(login_via_proxy "nobody@example.test" "$WRONG_PASSWORD")"
  if [ "$CODE" = 429 ] && grep -qi '^content-type: text/html' "$HEADER_FILE"; then PROXY_HTML=$((PROXY_HTML + 1)); fi
done
[ "$PROXY_HTML" -ge 1 ] || fail "the reference proxy never applied its own first-layer 429"
pass "the reference proxy applies its own first-layer limit (HTML 429), distinct from the application's"

# --- Logs: effective origin, events, no secrets ---------------------------------------------------------------------
LOGS="$(docker compose logs --no-color auth-api 2>&1)"
grep -qF "$FORGED" <<<"$LOGS" && fail "the forged forwarded address reached the logs"
grep -E "RateLimitApplied: request limit 'login' applied to client $GATEWAY " <<<"$LOGS" >/dev/null \
  || fail "a direct request with a forged X-Forwarded-For was not attributed to the real peer ($GATEWAY)"
grep -E "RateLimitApplied: request limit 'reset-password' applied to client $GATEWAY " <<<"$LOGS" >/dev/null \
  || fail "a request through the proxy was not attributed to the original client ($GATEWAY)"
grep -qF "client $PROXY_ADDRESS " <<<"$LOGS" && fail "the proxy's own address was used as the client origin"
pass "the effective origin is the real peer directly and the original client through the proxy; the forged value never appears"

grep -Eq 'AccountLockedOut: account .* locked until [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00 \(source Login\) at .*; trace [0-9a-f]{32}' <<<"$LOGS" || fail "the AccountLockedOut event is missing"
for reason in UnknownAccount WrongPassword LockedOut; do
  grep -Eq "LoginFailed: reason $reason, user .* at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}" <<<"$LOGS" || fail "the LoginFailed($reason) event is missing"
done
for policy in login refresh forgot-password reset-password forgot-password-address; do
  grep -Eq "RateLimitApplied: request limit '$policy' applied .*at [0-9]{4}-[0-9]{2}-[0-9]{2}T.*\+00:00; trace [0-9a-f]{32}" <<<"$LOGS" || fail "the RateLimitApplied('$policy') event is missing"
done
for secret in "$MEMBER_PASSWORD" "$WRONG_PASSWORD" "$ADMIN_TOKEN" "$AUTH_SMTP_PASSWORD" "PRIVATE KEY" "auth_refresh=" "$MEMBER_EMAIL"; do
  if grep -qF -- "$secret" <<<"$LOGS"; then fail "a secret or account email appears in the auth-api logs"; fi
done
pass "auth-api logs hold the lockout, login-failure, and rate-limit events with UTC time and trace, and no secret or email"

docker compose down -v >/dev/null

# --- Regression: Phase 6 acceptance (which runs Phases 5, 4, 3, 2 and 1), with this script's settings unset ----------------
(
  unset COMPOSE_FILE REFERENCE_PROXY_HTTP_PORT AUTH_JWT_PUBLIC_KEY_HOST_FILE AUTH_JWT_CLOCK_SKEW_SECONDS API_A_HTTP_PORT API_B_HTTP_PORT \
        COMPOSE_PROJECT_NAME AUTH_HTTP_PORT AUTH_SQLITE_HOST_PATH AUTH_RSA_HOST_PATH AUTH_JWT_ISSUER AUTH_JWT_AUDIENCE \
        AUTH_FRONTEND_ORIGIN AUTH_DATAPROTECTION_HOST_PATH AUTH_SMTP_HOST AUTH_SMTP_PORT AUTH_SMTP_SECURITY AUTH_SMTP_USERNAME \
        AUTH_SMTP_PASSWORD AUTH_SMTP_SENDER_ADDRESS AUTH_SMTP_SENDER_NAME \
        AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT AUTH_RATE_LIMIT_LOGIN_WINDOW_SECONDS AUTH_RATE_LIMIT_REFRESH_PERMIT_LIMIT \
        AUTH_RATE_LIMIT_REFRESH_WINDOW_SECONDS AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT AUTH_RATE_LIMIT_FORGOT_PASSWORD_WINDOW_SECONDS \
        AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT AUTH_RATE_LIMIT_RESET_PASSWORD_WINDOW_SECONDS \
        AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_PERMIT_LIMIT AUTH_RATE_LIMIT_FORGOT_PASSWORD_ADDRESS_WINDOW_SECONDS \
        AUTH_LOCKOUT_MAX_FAILED_ATTEMPTS AUTH_LOCKOUT_DURATION AUTH_TRUSTED_PROXIES AUTH_TRUSTED_NETWORKS \
        AUTH_INTERNAL_SUBNET FRONTEND_INTERNAL_ADDRESS
  "$REPO_ROOT/tests/acceptance/phase-6.sh"
) || fail "Phase 6 regression failed"
pass "Phase 6 acceptance regression (includes Phases 5, 4, 3, 2 and 1)"

echo "Phase 7 acceptance: ALL PASS"
