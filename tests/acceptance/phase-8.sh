#!/usr/bin/env bash
# Phase 8 disposable acceptance of the production topology (Gate G8 deployment evidence).
# Runs compose.yml as delivered (no direct-access override) plus only the Phase 6 mail-sink override, on
# disposable storage, and talks to the system exclusively through the HTTPS entry point
# https://localhost:$FRONTEND_HTTPS_PORT with a disposable self-signed certificate.
# Deployment section: exactly four permanent services; schema and administrator created by auth-api itself;
# static files served; every public URL translated to its unchanged internal route; created resources located
# under the public prefix; internal and redundant routes unreachable; backend ports unpublished; the private key
# restricted to auth-api and never visible to the consumers.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase8-acceptance.XXXXXX")"
RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:10.0"

export FRONTEND_HTTPS_PORT="${FRONTEND_HTTPS_PORT:-18443}"
export FRONTEND_HTTP_PORT="${FRONTEND_HTTP_PORT:-18088}"
ENTRY="https://localhost:${FRONTEND_HTTPS_PORT}"

export AUTH_SQLITE_HOST_PATH="$STATE/data"
export AUTH_RSA_HOST_PATH="$STATE/keys"
export AUTH_DATAPROTECTION_HOST_PATH="$STATE/dataprotection"
# Outside the restricted key directory: only this public file is mounted into the consumers.
export AUTH_JWT_PUBLIC_KEY_HOST_FILE="$STATE/public/jwt-public.pem"
export AUTH_JWT_ISSUER="https://auth-api.acceptance"
export AUTH_JWT_AUDIENCE="authentication-clients"
export AUTH_JWT_CLOCK_SKEW_SECONDS="30"
export AUTH_FRONTEND_ORIGIN="$ENTRY"
export AUTH_SMTP_HOST="mail-sink"
export AUTH_SMTP_PORT="1025"
export AUTH_SMTP_SECURITY="None"
export AUTH_SMTP_USERNAME="smtp-user"
export AUTH_SMTP_PASSWORD="smtp-secret-pw"
export AUTH_SMTP_SENDER_ADDRESS="no-reply@acceptance.invalid"
export AUTH_SMTP_SENDER_NAME="Authentication API Acceptance"
export MAIL_SINK_HTTP_PORT="${MAIL_SINK_HTTP_PORT:-18025}"
export COMPOSE_PROJECT_NAME="auth-api-phase8-acceptance"
# Production compose.yml plus the acceptance-only mail sink; deliberately no direct-access override.
export COMPOSE_FILE="compose.yml:tests/acceptance/compose.mail-sink.yml"
DEPLOYMENT_ENV_DIRECT_ACCESS=0
source "$REPO_ROOT/tests/acceptance/deployment-env.sh"

ADMIN_EMAIL="admin@local.invalid"
ADMIN_PASSWORD="admin"
MEMBER_EMAIL="member@example.test"
MEMBER_PASSWORD="Accept4nce!"
WRONG_PASSWORD="Wr0ng-Guess!"
TEST_PAGE_MARKER="acceptance-test-page"

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; exit 1; }

cleanup() {
  docker compose down -v >/dev/null 2>&1 || true
  # The key directory belongs to the container user with mode 0700; remove it as root in a throwaway container.
  docker run --rm --user root --entrypoint sh -v "$STATE:/state" "$RUNTIME_IMAGE" -c 'rm -rf /state/keys' >/dev/null 2>&1 || true
  rm -rf "$STATE"
}
trap cleanup EXIT

BODY_FILE="$STATE/body.out"
HEADER_FILE="$STATE/headers.out"

# request METHOD PATH [extra curl args...]: prints the status; body in $BODY_FILE, headers in $HEADER_FILE.
request() {
  local method="$1" path="$2"; shift 2
  curl --silent --cacert "$FRONTEND_TLS_HOST_PATH/tls.crt" --output "$BODY_FILE" --dump-header "$HEADER_FILE" \
    --write-out '%{http_code}' --request "$method" "$@" "$ENTRY$path"
}
json() { local method="$1" path="$2" body="$3"; shift 3; request "$method" "$path" --header 'Content-Type: application/json' --data "$body" "$@"; }
header_value() { { grep -i "^$1:" "$HEADER_FILE" || true; } | head -n1 | cut -d: -f2- | tr -d '\r' | sed 's/^ *//'; }
login() { json POST /auth/login "$(jq -cn --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"; }
key_modes() {
  docker run --rm --user root --entrypoint sh -v "$STATE/keys:/keys:ro" "$RUNTIME_IMAGE" \
    -c 'stat -c "%u:%a" /keys /keys/jwt-private.pem' | tr '\n' ' ' | sed 's/ $//'
}
wait_ready() {
  for _ in $(seq 1 90); do
    if docker compose exec -T frontend wget -qO- http://auth-api:8080/health/ready >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

# --- Disposable storage; the private key gets the restricted production ownership and mode -----------------
install -d -m 0777 "$STATE/data" "$STATE/dataprotection"
install -d -m 0755 "$STATE/keys" "$STATE/public"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$STATE/keys/jwt-private.pem" 2>/dev/null
openssl pkey -in "$STATE/keys/jwt-private.pem" -pubout -out "$AUTH_JWT_PUBLIC_KEY_HOST_FILE"
chmod 0644 "$AUTH_JWT_PUBLIC_KEY_HOST_FILE"
APP_UID="$(docker run --rm --entrypoint sh "$RUNTIME_IMAGE" -c 'echo $APP_UID')"
docker run --rm --user root --entrypoint sh -v "$STATE/keys:/keys" "$RUNTIME_IMAGE" \
  -c "chown -R $APP_UID:$APP_UID /keys && chmod 0700 /keys && chmod 0600 /keys/jwt-private.pem"
EXPECTED_KEY_MODES="$APP_UID:700 $APP_UID:600"
[ "$(key_modes)" = "$EXPECTED_KEY_MODES" ] || fail "the private key does not have the restricted ownership and modes before startup"
pass "private key owned by the container user (uid $APP_UID), file 0600 in a 0700 directory"

# --- Startup from empty storage with compose.yml alone ---------------------------------------------------------
[ "$(docker compose -f compose.yml config --services | sort | tr '\n' ' ')" = "api-a api-b auth-api frontend " ] \
  || fail "compose.yml does not define exactly frontend, auth-api, api-a and api-b"
docker compose up --build -d >/dev/null
wait_ready || fail "auth-api did not become ready"
[ "$(docker compose ps --services --status running | sort | tr '\n' ' ')" = "api-a api-b auth-api frontend mail-sink " ] \
  || fail "the running services are not the four permanent ones plus the acceptance mail sink"
[ -f "$STATE/data/auth.db" ] || fail "auth-api did not create its database on the mounted storage"
pass "compose.yml alone defines exactly four services; from empty storage auth-api migrated, bootstrapped and reports ready"

# --- Entry point: static files, HTTPS redirect --------------------------------------------------------------------
[ "$(request GET /)" = 200 ] && grep -q "$TEST_PAGE_MARKER" "$BODY_FILE" || fail "the static test page is not served at /"
[ "$(request GET /some/client/route)" = 200 ] && grep -q "$TEST_PAGE_MARKER" "$BODY_FILE" || fail "the single-page-application fallback is missing"
REDIRECT_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code} %{redirect_url}' "http://localhost:${FRONTEND_HTTP_PORT}/auth/login")"
case "$REDIRECT_STATUS" in "301 https://"*) ;; *) fail "plain HTTP is not redirected to HTTPS ($REDIRECT_STATUS)";; esac
pass "the entry point serves the application files over HTTPS and redirects plain HTTP"

# --- Public URL contract: /auth/<operation> -> /api/auth/<operation>, /auth/admin/* -> /api/admin/* -----------------
[ "$(login nobody@example.test "$WRONG_PASSWORD")" = 401 ] || fail "POST /auth/login did not reach the login route"
grep -qi '^content-type: application/problem+json' "$HEADER_FILE" && jq -e '.detail == "Invalid credentials."' "$BODY_FILE" >/dev/null \
  || fail "POST /auth/login did not answer with the login route's own problem details"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 200 ] || fail "the initial administrator cannot sign in through /auth/login"
ADMIN_TOKEN="$(jq -r .accessToken "$BODY_FILE")"
BEARER=(--header "Authorization: Bearer $ADMIN_TOKEN")
[ "$(request GET /auth/admin/users "${BEARER[@]}")" = 200 ] && jq -e 'type == "array" and length == 1' "$BODY_FILE" >/dev/null \
  || fail "GET /auth/admin/users did not reach the user list"
[ "$(request GET /auth/admin/roles "${BEARER[@]}")" = 200 ] && jq -e 'type == "array"' "$BODY_FILE" >/dev/null \
  || fail "GET /auth/admin/roles did not reach the role list"
[ "$(request GET /auth/admin/users)" = 401 ] || fail "the administrative routes are reachable without a token"
[ "$(json POST /auth/admin/users "$(jq -cn --arg e "$MEMBER_EMAIL" --arg p "$MEMBER_PASSWORD" '{email:$e,password:$p}')" "${BEARER[@]}")" = 201 ] \
  || fail "POST /auth/admin/users did not create a user"
LOCATION="$(header_value Location)"
case "$LOCATION" in /auth/admin/users/*) ;; *) fail "the created user's location is not public ($LOCATION)";; esac
[ "$(request GET "$LOCATION" "${BEARER[@]}")" = 200 ] && jq -e --arg e "$MEMBER_EMAIL" '.email == $e' "$BODY_FILE" >/dev/null \
  || fail "the public location of the created user does not resolve"
[ "$(json POST /auth/change-password '{"currentPassword":"x","newPassword":"y"}')" = 401 ] || fail "POST /auth/change-password did not reach its route"
[ "$(json POST /auth/forgot-password '{"email":"nobody@example.test"}')" = 204 ] || fail "POST /auth/forgot-password did not reach its route"
[ "$(json POST /auth/reset-password '{"email":"nobody@example.test","token":"not-a-token","newPassword":"N3w-Secret!"}')" = 401 ] \
  || fail "POST /auth/reset-password did not reach its route"
[ "$(request POST /auth/refresh)" = 403 ] || fail "POST /auth/refresh without Origin was not refused by the origin check"
[ "$(request POST /auth/refresh --header "Origin: $ENTRY")" = 401 ] || fail "POST /auth/refresh with the frontend origin did not reach the credential check"
[ "$(request POST /auth/logout --header "Origin: $ENTRY")" = 204 ] || fail "POST /auth/logout did not reach its route"
for service in api-a api-b; do
  [ "$(request GET "/$service/api/caller" "${BEARER[@]}")" = 200 ] && jq -e --arg s "$service" '.service == $s' "$BODY_FILE" >/dev/null \
    || fail "GET /$service/api/caller did not reach $service with a valid token"
done
pass "every public URL reaches its unchanged internal route; created users are located under /auth/admin/"

# --- Redundant and internal routes are not served -------------------------------------------------------------------
for path in "POST /auth/api/auth/login" "GET /auth/health/ready" "GET /auth/health/live" "GET /auth/openapi/v1.json" \
            "GET /auth/scalar" "GET /auth" "GET /api-a/health/live" "GET /api-b/health/live" "GET /api-a/api"; do
  status="$(request ${path%% *} "${path#* }" "${BEARER[@]}")"
  [ "$status" = 404 ] || fail "$path answered $status instead of 404"
  grep -q "$TEST_PAGE_MARKER" "$BODY_FILE" && fail "$path fell through to the static page"
done
pass "redundant (/auth/api/auth/login) and internal (health, OpenAPI, Scalar) routes answer 404 through the entry point"

# --- Isolation: no backend port, private key only in auth-api -------------------------------------------------------
for service in auth-api api-a api-b; do
  container="$(docker compose ps -q "$service")"
  [ "$(docker inspect --format '{{json .HostConfig.PortBindings}}' "$container")" = "{}" ] \
    || fail "$service has host port bindings"
  [ "$(docker compose ps --format json "$service" | jq -r '[.Publishers[]? | select(.PublishedPort > 0)] | length')" = 0 ] \
    || fail "$service publishes a port to the host"
done
docker compose exec -T auth-api test -r /var/lib/auth-api/keys/jwt-private.pem || fail "auth-api cannot read its private key"
for service in api-a api-b; do
  [ "$(docker compose exec -T "$service" ls -A /var/lib/consumer)" = "jwt-public.pem" ] || fail "$service mounts more than the public key"
  if docker compose exec -T "$service" sh -c 'test -e /var/lib/auth-api || grep -rqs "PRIVATE KEY" /var/lib/consumer /app'; then
    fail "$service can see private key material"
  fi
done
[ "$(key_modes)" = "$EXPECTED_KEY_MODES" ] || fail "the private key's ownership or modes changed while running"
pass "no backend publishes a port; auth-api signs with its restricted private key and the consumers hold only the public key"

echo "Phase 8 acceptance (deployment section): PASS"
