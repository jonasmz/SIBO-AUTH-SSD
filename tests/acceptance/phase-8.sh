#!/usr/bin/env bash
# Phase 8 disposable acceptance of the production topology (Gate G8 deployment evidence).
# Runs compose.yml as delivered (no direct-access override) plus only the Phase 6 mail-sink override, on
# disposable storage, and talks to the system exclusively through the HTTPS entry point
# https://localhost:$FRONTEND_HTTPS_PORT with a disposable self-signed certificate.
# Deployment section: exactly four permanent services; schema and administrator created by auth-api itself;
# static files served; every public URL translated to its unchanged internal route; created resources located
# under the public prefix; internal and redundant routes unreachable; backend ports unpublished; the private key
# restricted to auth-api and never visible to the consumers.
# Lifecycle section: database, key ring, private key, logs, users, roles, the changed administrator password and an
# active refresh session survive restart, rebuild, recreation and `down -v`; a SQLite backup taken while the
# service is writing passes the integrity check and restores into a separate disposable project.
# End-to-end section: the whole flow through the public URLs only, with a cookie jar acting as the browser, followed
# by the log review and the chained Phase 7 -> 1 regression.
# Requires: docker compose, openssl, curl, jq. Uses only disposable directories under $TMPDIR.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

STATE="$(mktemp -d "${TMPDIR:-/tmp}/auth-api-phase8-acceptance.XXXXXX")"
RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:10.0"

export FRONTEND_HTTPS_PORT="${FRONTEND_HTTPS_PORT:-18443}"
export FRONTEND_HTTP_PORT="${FRONTEND_HTTP_PORT:-18088}"
# Not the 172.30.x default of compose.yml: hosts often have a route (VPN, other bridge) over it.
export AUTH_INTERNAL_SUBNET="${AUTH_INTERNAL_SUBNET:-172.29.80.0/24}"
export FRONTEND_INTERNAL_ADDRESS="${FRONTEND_INTERNAL_ADDRESS:-172.29.80.10}"
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
SINK="http://localhost:${MAIL_SINK_HTTP_PORT}"
# Long enough that tokens obtained before the lifecycle operations are still valid after them.
export AUTH_JWT_LIFETIME_MINUTES="60"
# The proxy's first layer (60 r/min, burst 20) is exercised on purpose later; these keep the application's own
# limits out of the way of the other sections. The login limit is lowered for the trust check (see there).
export AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT="1000"
export AUTH_RATE_LIMIT_LOGIN_WINDOW_SECONDS="60"
export AUTH_RATE_LIMIT_FORGOT_PASSWORD_PERMIT_LIMIT="50"
export AUTH_RATE_LIMIT_RESET_PASSWORD_PERMIT_LIMIT="50"
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

RESTORE_PROJECT="auth-api-phase8-restore"
SQLITE_IMAGE="auth-api-phase8-acceptance-sqlite:local"

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; exit 1; }

cleanup() {
  docker compose down -v >/dev/null 2>&1 || true
  COMPOSE_PROJECT_NAME="$RESTORE_PROJECT" docker compose down -v >/dev/null 2>&1 || true
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
  # Stay under the proxy's first layer (1 request/second sustained) so no section meets it by accident.
  case "$path" in /auth/login|/auth/refresh|/auth/forgot-password|/auth/reset-password) sleep 1 ;; esac
  curl --silent --cacert "$FRONTEND_TLS_HOST_PATH/tls.crt" --output "$BODY_FILE" --dump-header "$HEADER_FILE" \
    --write-out '%{http_code}' --request "$method" "$@" "$ENTRY$path"
}
json() { local method="$1" path="$2" body="$3"; shift 3; request "$method" "$path" --header 'Content-Type: application/json' --data "$body" "$@"; }
header_value() { { grep -i "^$1:" "$HEADER_FILE" || true; } | head -n1 | cut -d: -f2- | tr -d '\r' | sed 's/^ *//'; }
login() { local email="$1" password="$2"; shift 2; json POST /auth/login "$(jq -cn --arg e "$email" --arg p "$password" '{email:$e,password:$p}')" "$@"; }
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


# ============================================================================================================
# Lifecycle: persistent state outlives restart, rebuild, recreation and `down -v` (US3)
# ============================================================================================================
ADMIN_PASSWORD_CHANGED="Adm1n-Changed-8!"
ADMIN_PASSWORD_FINAL="Adm1n-Final-Pass-8!"
MEMBER_ID="${LOCATION##*/}"
MEMBER_JAR="$STATE/member.jar"
ROLE_NAME="Operator"
SECRETS=("$MEMBER_PASSWORD" "$WRONG_PASSWORD" "$ADMIN_PASSWORD_CHANGED" "$ADMIN_PASSWORD_FINAL" "$AUTH_SMTP_PASSWORD" "PRIVATE KEY" "$ADMIN_TOKEN")
track_secret() { [ -z "$1" ] || SECRETS+=("$1"); }
cookie_value() { { grep -i '^set-cookie: auth_refresh=' "$HEADER_FILE" || true; } | head -n1 | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r'; }
access_token() { jq -r .accessToken "$BODY_FILE"; }
# Runs sqlite3 (the online-backup tool the operator uses) in a throwaway container as the container user, with the
# live database directory at /db and the backup directory at /backup.
sqlite() {
  docker run --rm --user "$APP_UID" -v "$STATE/data:/db" -v "$STATE/backup:/backup" -v "$STATE/restore-data:/restore" \
    "$SQLITE_IMAGE" sqlite3 -cmd '.timeout 10000' "$@"
}
install -d -m 0777 "$STATE/backup" "$STATE/restore-data"
docker image inspect "$SQLITE_IMAGE" >/dev/null 2>&1 \
  || printf 'FROM alpine:3.22\nRUN apk add --no-cache sqlite\n' | docker build -q -t "$SQLITE_IMAGE" - >/dev/null

# Establish identifiable state: changed administrator password, a role and an assignment, an active session.
[ "$(json POST /auth/change-password "$(jq -cn --arg c "$ADMIN_PASSWORD" --arg n "$ADMIN_PASSWORD_CHANGED" '{currentPassword:$c,newPassword:$n}')" "${BEARER[@]}")" = 204 ] \
  || fail "the administrator password could not be changed"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 401 ] || fail "the replaced administrator password still works"
ADMIN_PASSWORD="$ADMIN_PASSWORD_CHANGED"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 200 ] || fail "the changed administrator password does not work"
ADMIN_TOKEN="$(access_token)"; track_secret "$ADMIN_TOKEN"
BEARER=(--header "Authorization: Bearer $ADMIN_TOKEN")
[ "$(json POST /auth/admin/roles "$(jq -cn --arg n "$ROLE_NAME" '{name:$n}')" "${BEARER[@]}")" = 201 ] || fail "the role could not be created"
[ "$(json PUT "/auth/admin/users/$MEMBER_ID/roles" "$(jq -cn --arg n "$ROLE_NAME" '{roles:[$n]}')" "${BEARER[@]}")" = 200 ] || fail "the role could not be assigned"
[ "$(login "$MEMBER_EMAIL" "$MEMBER_PASSWORD" -b "$MEMBER_JAR" -c "$MEMBER_JAR")" = 200 ] || fail "the member could not sign in"
MEMBER_TOKEN="$(access_token)"; track_secret "$MEMBER_TOKEN"
track_secret "$(cookie_value)"
grep -q 'auth_refresh' "$MEMBER_JAR" || fail "the member's refresh session is not in the cookie jar"

assert_state() {
  local label="$1"
  [ -s "$STATE/data/auth.db" ] || fail "$label: the database is gone"
  [ "$(key_modes)" = "$EXPECTED_KEY_MODES" ] || fail "$label: the private key is gone or its ownership or modes changed"
  docker run --rm --user root --entrypoint sh -v "$AUTH_DATAPROTECTION_HOST_PATH:/keys:ro" "$RUNTIME_IMAGE" -c 'ls /keys/*.xml >/dev/null 2>&1' \
    || fail "$label: the key ring is gone"
  ls "$AUTH_LOGS_HOST_PATH"/auth-*.log >/dev/null 2>&1 || fail "$label: the log files are gone"
  [ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD_CHANGED")" = 200 ] || fail "$label: the changed administrator password does not work"
  local token; token="$(access_token)"
  local bearer=(--header "Authorization: Bearer $token")
  [ "$(login "$ADMIN_EMAIL" "admin")" = 401 ] || fail "$label: the initial administrator password works again"
  [ "$(request GET /auth/admin/users "${bearer[@]}")" = 200 ] \
    && jq -e --arg e "$MEMBER_EMAIL" --arg r "$ROLE_NAME" 'any(.[]; .email == $e and ((.roles // []) | index($r)))' "$BODY_FILE" >/dev/null \
    || fail "$label: the user or its role assignment is gone"
  [ "$(request GET /auth/admin/roles "${bearer[@]}")" = 200 ] && jq -e --arg r "$ROLE_NAME" 'any(.[]; .name == $r)' "$BODY_FILE" >/dev/null \
    || fail "$label: the role is gone"
  # The session created before the operation still renews, and its token (signed with the persisted key) validates.
  [ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$MEMBER_JAR" -c "$MEMBER_JAR")" = 200 ] || fail "$label: the active refresh session was lost"
  for service in api-a api-b; do
    for candidate in "$MEMBER_TOKEN" "$token"; do
      [ "$(request GET "/$service/api/caller" --header "Authorization: Bearer $candidate")" = 200 ] \
        || fail "$label: $service rejects a token signed with the persisted key"
    done
  done
  pass "$label: database, key ring, private key, logs, users, role, changed password, session and token validation intact"
}

assert_state "baseline"
docker compose restart >/dev/null
wait_ready || fail "auth-api was not ready after restart"
assert_state "docker compose restart"
docker compose up --build -d >/dev/null
wait_ready || fail "auth-api was not ready after up --build"
assert_state "docker compose up --build"
docker compose up -d --force-recreate >/dev/null
wait_ready || fail "auth-api was not ready after up --force-recreate"
assert_state "docker compose up --force-recreate"
docker compose down -v >/dev/null
[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT_NAME")" ] || fail "a Compose-managed volume exists"
docker compose up -d >/dev/null
wait_ready || fail "auth-api was not ready after down -v and up"
assert_state "docker compose down -v + up"

# ============================================================================================================
# Backup under writes and restore into a separate disposable project (US3)
# ============================================================================================================
WRITER_JAR="$STATE/writer.jar"
writer_loop() {
  local body="$STATE/writer.body" iteration
  for iteration in $(seq 1 30); do
    curl --silent --output "$body" --cacert "$FRONTEND_TLS_HOST_PATH/tls.crt" -b "$WRITER_JAR" -c "$WRITER_JAR" \
      --header 'Content-Type: application/json' --data "$(jq -cn --arg e "$ADMIN_EMAIL" --arg p "$ADMIN_PASSWORD" '{email:$e,password:$p}')" "$ENTRY/auth/login" || true
    sleep 1
    curl --silent --output "$body" --cacert "$FRONTEND_TLS_HOST_PATH/tls.crt" -b "$WRITER_JAR" -c "$WRITER_JAR" \
      --request POST --header "Origin: $ENTRY" "$ENTRY/auth/refresh" || true
    sleep 1
  done
}
FAMILIES_BEFORE="$(sqlite /db/auth.db 'SELECT COUNT(*) FROM RenewableSessionFamilies;')"
writer_loop &
WRITER_PID=$!
for _ in $(seq 1 30); do
  [ "$(sqlite /db/auth.db 'SELECT COUNT(*) FROM RenewableSessionFamilies;')" -gt "$FAMILIES_BEFORE" ] && break
  sleep 1
done
[ "$(sqlite /db/auth.db 'SELECT COUNT(*) FROM RenewableSessionFamilies;')" -gt "$FAMILIES_BEFORE" ] || fail "the writer loop produced no writes"
sqlite /db/auth.db ".backup '/backup/auth.db'" || fail "the online backup failed"
kill -0 "$WRITER_PID" 2>/dev/null || fail "the writer loop ended before the backup finished, so it was not taken under writes"
[ "$(sqlite /backup/auth.db 'PRAGMA integrity_check;')" = ok ] || fail "the backup does not pass PRAGMA integrity_check"
wait "$WRITER_PID" || true
BACKUP_USERS="$(sqlite /backup/auth.db 'SELECT COUNT(*) FROM AspNetUsers;')"
BACKUP_FAMILIES="$(sqlite /backup/auth.db 'SELECT COUNT(*) FROM RenewableSessionFamilies;')"
[ "$BACKUP_USERS" -ge 2 ] && [ "$BACKUP_FAMILIES" -gt "$FAMILIES_BEFORE" ] || fail "the backup lacks the established state"
pass "a SQLite backup taken while the service was writing passes the integrity check ($BACKUP_USERS users, $BACKUP_FAMILIES session families)"

# Restore: a fresh disposable project (own name, network, address and ports) on a data directory holding only the backup.
RESTORE_ROOT="$STATE/restore"
install -d -m 0777 "$RESTORE_ROOT/dataprotection" "$RESTORE_ROOT/logs"
cp "$STATE/backup/auth.db" "$STATE/restore-data/auth.db"
chmod 0666 "$STATE/restore-data/auth.db"
(
  export COMPOSE_PROJECT_NAME="$RESTORE_PROJECT"
  export AUTH_INTERNAL_SUBNET="172.29.81.0/24" FRONTEND_INTERNAL_ADDRESS="172.29.81.10" AUTH_TRUSTED_PROXIES="172.29.81.10"
  export FRONTEND_HTTPS_PORT="18444" FRONTEND_HTTP_PORT="18089" MAIL_SINK_HTTP_PORT="18026"
  export AUTH_SQLITE_HOST_PATH="$STATE/restore-data" AUTH_DATAPROTECTION_HOST_PATH="$RESTORE_ROOT/dataprotection" AUTH_LOGS_HOST_PATH="$RESTORE_ROOT/logs"
  export AUTH_FRONTEND_ORIGIN="https://localhost:18444"
  ENTRY="https://localhost:18444"
  trap 'docker compose down -v >/dev/null 2>&1 || true' EXIT
  docker compose up --build -d >/dev/null
  wait_ready || fail "the restored instance did not become ready"
  [ "$(sqlite /restore/auth.db 'PRAGMA integrity_check;')" = ok ] || fail "the restored database does not pass PRAGMA integrity_check"
  [ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 200 ] || fail "the restored instance rejects the known administrator login"
  RESTORED_TOKEN="$(access_token)"
  [ "$(request GET /auth/admin/users --header "Authorization: Bearer $RESTORED_TOKEN")" = 200 ] \
    && jq -e --arg e "$MEMBER_EMAIL" --arg r "$ROLE_NAME" 'any(.[]; .email == $e and ((.roles // []) | index($r)))' "$BODY_FILE" >/dev/null \
    || fail "the restored instance lacks the user or its role"
  [ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$MEMBER_JAR" -c "$MEMBER_JAR")" = 200 ] || fail "the persisted session was not restored"
  [ "$(sqlite /restore/auth.db 'SELECT COUNT(*) FROM RenewableSessionFamilies;')" -ge "$BACKUP_FAMILIES" ] || fail "the restored database lost session rows"
) || fail "the restore into a separate project failed"
pass "the backup restored into a separate disposable project: integrity ok, known login, user, role and persisted session intact"

# ============================================================================================================
# End to end through the public URLs, with a cookie jar acting as the browser (US4)
# ============================================================================================================
E2E_EMAIL="e2e@example.test"
E2E_PASSWORD="E2e-Passw0rd-8!"
E2E_RESET_PASSWORD="E2e-Reset-Passw0rd-8!"
E2E_ROLE="Reviewer"
BROWSER_JAR="$STATE/browser.jar"
SECRETS+=("$E2E_PASSWORD" "$E2E_RESET_PASSWORD")
set_cookie_line() { { grep -i '^set-cookie: auth_refresh=' "$HEADER_FILE" || true; } | head -n1 | tr -d '\r'; }
assert_cookie_attributes() {
  local line="$1" what="$2"
  for attribute in 'Path=/auth' 'HttpOnly' 'Secure' 'SameSite=Strict'; do
    grep -qi -- "$attribute" <<<"$line" || fail "$what lacks $attribute"
  done
  if grep -qi 'Path=/api' <<<"$line"; then fail "$what leaks the internal path"; fi
  if grep -qi 'domain=' <<<"$line"; then fail "$what sets a Domain"; fi
}

# Administrator: login and password replacement.
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD_CHANGED")" = 200 ] || fail "administrator login failed"
ADMIN_TOKEN="$(access_token)"; track_secret "$ADMIN_TOKEN"
BEARER=(--header "Authorization: Bearer $ADMIN_TOKEN")
[ "$(json POST /auth/change-password "$(jq -cn --arg c "$ADMIN_PASSWORD_CHANGED" --arg n "$ADMIN_PASSWORD_FINAL" '{currentPassword:$c,newPassword:$n}')" "${BEARER[@]}")" = 204 ] \
  || fail "the administrator password replacement failed"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD_CHANGED")" = 401 ] || fail "the replaced password is still accepted"
ADMIN_PASSWORD="$ADMIN_PASSWORD_FINAL"
[ "$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")" = 200 ] || fail "the new administrator password is rejected"
ADMIN_TOKEN="$(access_token)"; track_secret "$ADMIN_TOKEN"
BEARER=(--header "Authorization: Bearer $ADMIN_TOKEN")

# User creation and role assignment.
[ "$(json POST /auth/admin/users "$(jq -cn --arg e "$E2E_EMAIL" --arg p "$E2E_PASSWORD" '{email:$e,password:$p}')" "${BEARER[@]}")" = 201 ] || fail "user creation failed"
E2E_ID="$(header_value Location)"; E2E_ID="${E2E_ID##*/}"
[ "$(json POST /auth/admin/roles "$(jq -cn --arg n "$E2E_ROLE" '{name:$n}')" "${BEARER[@]}")" = 201 ] || fail "role creation failed"
[ "$(json PUT "/auth/admin/users/$E2E_ID/roles" "$(jq -cn --arg n "$E2E_ROLE" '{roles:[$n]}')" "${BEARER[@]}")" = 200 ] || fail "role assignment failed"

# The browser signs in: restrictive cookie under the public path; both consumers accept the token.
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 200 ] || fail "the user could not sign in"
USER_TOKEN="$(access_token)"; track_secret "$USER_TOKEN"
assert_cookie_attributes "$(set_cookie_line)" "the login cookie"
COOKIE_1="$(cookie_value)"; track_secret "$COOKIE_1"
for service in api-a api-b; do
  [ "$(request GET "/$service/api/caller" --header "Authorization: Bearer $USER_TOKEN")" = 200 ] \
    && jq -e --arg s "$service" --arg r "$E2E_ROLE" '.service == $s and (.roles | index($r))' "$BODY_FILE" >/dev/null \
    || fail "$service did not accept the user's token with its role"
done
pass "login sets auth_refresh with Path=/auth, HttpOnly, Secure and SameSite=Strict; API A and API B accept the token"

# Refresh rotation under the same public path; foreign origin refused; replay of the old value refused.
[ "$(request POST /auth/refresh --header "Origin: https://evil.example" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 403 ] || fail "a refresh from a foreign Origin was not refused"
[ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 200 ] || fail "the refresh did not succeed"
assert_cookie_attributes "$(set_cookie_line)" "the rotated cookie"
COOKIE_2="$(cookie_value)"; track_secret "$COOKIE_2"
[ -n "$COOKIE_2" ] && [ "$COOKIE_2" != "$COOKIE_1" ] || fail "the refresh cookie was not rotated"
[ "$(request POST /auth/refresh --header "Origin: $ENTRY" --header "Cookie: auth_refresh=$COOKIE_1")" = 401 ] || fail "the old cookie value was accepted after rotation"
[ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 401 ] || fail "the session survived the replay of its old credential"
pass "refresh rotates the cookie under /auth; a foreign Origin is refused; replaying the old value is refused and revokes the session"

# Logout clears the cookie under the public path; the cleared jar cannot renew.
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 200 ] || fail "the re-login failed"
track_secret "$(cookie_value)"
[ "$(request POST /auth/logout --header "Origin: $ENTRY" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 204 ] || fail "logout failed"
CLEARED="$(set_cookie_line)"
grep -qi 'auth_refresh=;' <<<"$CLEARED" && grep -qi 'Path=/auth' <<<"$CLEARED" && ! grep -qi 'Path=/api' <<<"$CLEARED" || fail "logout did not clear the cookie under Path=/auth"
[ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$BROWSER_JAR" -c "$BROWSER_JAR")" = 401 ] || fail "the cleared jar could still renew"
pass "logout clears the cookie under Path=/auth and the cleared jar can no longer renew"

# Password recovery through the mail sink; every earlier session is revoked.
curl --silent --request DELETE "$SINK/api/v1/messages" >/dev/null
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD" -b "$STATE/session-a.jar" -c "$STATE/session-a.jar")" = 200 ] || fail "session A could not sign in"
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD" -b "$STATE/session-b.jar" -c "$STATE/session-b.jar")" = 200 ] || fail "session B could not sign in"
[ "$(json POST /auth/forgot-password "$(jq -cn --arg e "$E2E_EMAIL" '{email:$e}')")" = 204 ] || fail "forgot-password failed"
for _ in $(seq 1 30); do
  [ "$(curl --silent "$SINK/api/v1/messages" | jq '.messages | length')" -ge 1 ] && break
  sleep 1
done
MESSAGE_ID="$(curl --silent "$SINK/api/v1/messages" | jq -r '.messages[0].ID')"
RESET_TOKEN="$(curl --silent "$SINK/api/v1/message/$MESSAGE_ID" | jq -r '.Text' | grep '^Token:' | head -n1 | sed 's/^Token:[[:space:]]*//' | tr -d '\r')"
[ -n "$RESET_TOKEN" ] || fail "the recovery email carries no token"
track_secret "$RESET_TOKEN"
[ "$(json POST /auth/reset-password "$(jq -cn --arg e "$E2E_EMAIL" --arg t "$RESET_TOKEN" --arg p "$E2E_RESET_PASSWORD" '{email:$e,token:$t,newPassword:$p}')")" = 204 ] \
  || fail "the reset token was not accepted"
for jar in session-a session-b; do
  [ "$(request POST /auth/refresh --header "Origin: $ENTRY" -b "$STATE/$jar.jar" -c "$STATE/$jar.jar")" = 401 ] || fail "$jar survived the password reset"
done
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD")" = 401 ] || fail "the replaced password is still accepted"
[ "$(login "$E2E_EMAIL" "$E2E_RESET_PASSWORD")" = 200 ] || fail "the reset password is rejected"
pass "forgot-password delivers a token through the mail sink; the reset revokes the earlier sessions and replaces the password"

# Disablement: the user is rejected until re-enabled; role removal.
E2E_PASSWORD="$E2E_RESET_PASSWORD"
[ "$(request POST "/auth/admin/users/$E2E_ID/disable" "${BEARER[@]}")" = 200 ] || fail "the user could not be disabled"
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD")" = 401 ] || fail "a disabled user could sign in"
[ "$(request POST "/auth/admin/users/$E2E_ID/enable" "${BEARER[@]}")" = 200 ] || fail "the user could not be re-enabled"
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD")" = 200 ] || fail "the re-enabled user cannot sign in"
[ "$(json PUT "/auth/admin/users/$E2E_ID/roles" '{"roles":[]}' "${BEARER[@]}")" = 200 ] || fail "the role could not be removed"
pass "a disabled user is rejected at login; re-enabling and role removal take effect"

# Account lockout and its recovery without waiting: the persisted lockout end is moved into the past.
for _ in 1 2 3 4 5; do
  [ "$(login "$E2E_EMAIL" "$WRONG_PASSWORD")" = 401 ] || fail "a wrong password was not 401"
done
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD")" = 401 ] || fail "the correct password worked while the account should be locked"
[ "$(sqlite /db/auth.db "SELECT COUNT(*) FROM AspNetUsers WHERE Id = '$E2E_ID' AND LockoutEnd IS NOT NULL;")" = 1 ] || fail "no lockout was persisted"
sqlite /db/auth.db "UPDATE AspNetUsers SET LockoutEnd = '2000-01-01 00:00:00.0000000+00:00' WHERE Id = '$E2E_ID';" >/dev/null
[ "$(login "$E2E_EMAIL" "$E2E_PASSWORD")" = 200 ] || fail "the account did not recover once its lockout ended"
pass "five wrong passwords lock the account; with the lockout end moved into the past the correct password works again"

# Application 429 (per-address recovery limit: 3 per hour), independent of the proxy's own layer.
for attempt in 1 2 3; do
  [ "$(json POST /auth/forgot-password '{"email":"limit@example.test"}')" = 204 ] || fail "recovery request $attempt for one address was not answered normally"
done
[ "$(json POST /auth/forgot-password '{"email":"limit@example.test"}')" = 429 ] || fail "the per-address recovery limit did not answer 429"
grep -qi '^content-type: application/problem+json' "$HEADER_FILE" || fail "the application 429 is not application/problem+json"
pass "the application answers 429 problem+json when a limit is exceeded"

# Trust behavior through the entry point: lower the login limit, recreate (frontend included, so Nginx resolves
# the new container), then send requests carrying a client-forged X-Forwarded-For.
FORGED="203.0.113.99"
AUTH_RATE_LIMIT_LOGIN_PERMIT_LIMIT=3 AUTH_RATE_LIMIT_LOGIN_WINDOW_SECONDS=300 docker compose up -d --force-recreate >/dev/null
wait_ready || fail "auth-api was not ready after recreation with the lower login limit"
STATUSES=""
for _ in 1 2 3 4; do
  STATUSES="$STATUSES $(login nobody@example.test "$WRONG_PASSWORD" --header "X-Forwarded-For: $FORGED" --header "CF-Connecting-IP: $FORGED")"
done
[ "${STATUSES# }" = "401 401 401 429" ] || fail "the forged-header requests were not limited as one client ('${STATUSES# }')"
grep -qi '^content-type: application/problem+json' "$HEADER_FILE" || fail "the login 429 is not application/problem+json"
CLIENT_ADDRESS="$(docker compose logs --no-color frontend 2>&1 | grep 'POST /auth/login' | tail -n1 | sed -E 's/^[^|]*\| *//' | awk '{print $1}')"
[[ "$CLIENT_ADDRESS" =~ ^[0-9.]+$ ]] || fail "could not read the client address the frontend saw ('$CLIENT_ADDRESS')"
[ "$CLIENT_ADDRESS" != "$FRONTEND_INTERNAL_ADDRESS" ] || fail "the frontend saw its own address as the client"

# The proxy's own first layer: a burst beyond its allowance is answered by Nginx itself (HTML), not the application.
FLOOD="$(seq 1 60 | xargs -P 30 -I{} curl --silent --output /dev/null --cacert "$FRONTEND_TLS_HOST_PATH/tls.crt" \
  --write-out '%{http_code} %{content_type}\n' --request POST --header 'Content-Type: application/json' \
  --data '{"email":"nobody@example.test","password":"x"}' "$ENTRY/auth/login")"
grep -q '^429 text/html' <<<"$FLOOD" || fail "the proxy never applied its own first-layer 429"
grep -q '^429 application/problem+json' <<<"$FLOOD" || fail "the application's 429 was not distinguishable from the proxy's"
pass "forged X-Forwarded-For requests are limited as one client; the proxy's own HTML 429 is distinct from the application's problem+json 429"

# ============================================================================================================
# Log review (US4): every Roadmap §14.2 event, UTC time and trace identifier, no secret
# ============================================================================================================
LOG_FILE="$AUTH_LOGS_HOST_PATH/auth-$(date -u +%F).log"
[ -s "$LOG_FILE" ] || fail "the daily log file $(basename "$LOG_FILE") does not exist on the host path"
LOGS="$(cat "$LOG_FILE")"
assert_event() {
  local description="$1" pattern="$2"
  grep -Eq "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z \[[A-Za-z]+\] [^ ]+ trace=[0-9a-f]{32} .*$pattern.*; trace [0-9a-f]{32}" <<<"$LOGS" \
    || fail "the log lacks the $description event with UTC time and a trace identifier"
}
assert_event "login success" 'LoginSucceeded: user '
assert_event "login failure" 'LoginFailed: reason (WrongPassword|UnknownAccount|LockedOut)'
assert_event "account lockout" 'AccountLockedOut: account '
assert_event "rate limit" "RateLimitApplied: request limit 'login' applied to client $CLIENT_ADDRESS "
assert_event "logout" 'Renewable session family .* revoked'
assert_event "password change" 'Password changed for user '
assert_event "password reset" 'PasswordReset: password reset for user '
assert_event "user creation" 'UserCreated: user '
assert_event "user enablement" 'UserEnabled: user '
assert_event "user disablement" 'UserDisabled: user '
assert_event "role assignment" 'UserRoleAssigned: user '
assert_event "role removal" 'UserRoleRemoved: user '
assert_event "refresh reuse detection" 'Refresh credential replay detected'
assert_event "session revocation" 'Renewable sessions of user .* revoked'
grep -qF "client $FORGED " <<<"$LOGS" && fail "the forged forwarded address reached the log"
grep -qF "client $FRONTEND_INTERNAL_ADDRESS " <<<"$LOGS" && fail "the frontend's internal address was logged as the client"
CONSOLE="$(docker compose logs --no-color auth-api 2>&1)"
for secret in "${SECRETS[@]}"; do
  if grep -qF -- "$secret" <<<"$LOGS" || grep -qF -- "$secret" <<<"$CONSOLE"; then fail "a secret appears in the console output or the log file"; fi
done
grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z ' <<<"$LOGS" || fail "the log lines do not start with a UTC timestamp"
pass "the daily log holds every required event with UTC time and trace identifier, the client origin as the frontend saw it, and no secret (console included)"

# ============================================================================================================
# Teardown and regression: Phase 7 acceptance (which chains Phases 6 -> 1) with this script's settings unset
# ============================================================================================================
docker compose down -v >/dev/null
(
  # shellcheck disable=SC2046
  unset $(compgen -e | grep -E '^(AUTH_|FRONTEND_|COMPOSE_|MAIL_SINK|API_[AB]_|DEPLOYMENT_ENV|REFERENCE_PROXY)') STATE
  "$REPO_ROOT/tests/acceptance/phase-7.sh"
) || fail "Phase 7 regression failed"
pass "Phase 7 acceptance regression (includes Phases 6, 5, 4, 3, 2 and 1)"

echo "Phase 8 acceptance: ALL PASS"
