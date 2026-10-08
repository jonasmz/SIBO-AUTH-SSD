# Sourced by every acceptance script after it has created $STATE (and optionally set COMPOSE_FILE).
# compose.yml requires the persistent log directory and the frontend inputs even when a script starts only
# auth-api, because Compose validates every ${VAR:?} in the file. This creates disposable ones under $STATE and,
# unless DEPLOYMENT_ENV_DIRECT_ACCESS=0, appends the override that publishes the backend ports the Phase 1-7
# procedures address directly. It never points at real storage.
# Requires: openssl.

_deployment_env_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Persistent logs: writable by the container user, like the scripts' other disposable data directories.
export AUTH_LOGS_HOST_PATH="$STATE/logs"
install -d -m 0777 "$AUTH_LOGS_HOST_PATH"

# Minimal static test page standing in for the owner-supplied compiled Angular files.
export FRONTEND_STATIC_HOST_PATH="$STATE/frontend-static"
install -d -m 0755 "$FRONTEND_STATIC_HOST_PATH"
cp "$_deployment_env_dir/frontend-test-page/index.html" "$FRONTEND_STATIC_HOST_PATH/index.html"

# Disposable self-signed certificate for the HTTPS entry point (valid for localhost).
export FRONTEND_TLS_HOST_PATH="$STATE/frontend-tls"
install -d -m 0755 "$FRONTEND_TLS_HOST_PATH"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
  -keyout "$FRONTEND_TLS_HOST_PATH/tls.key" -out "$FRONTEND_TLS_HOST_PATH/tls.crt" 2>/dev/null
chmod 0644 "$FRONTEND_TLS_HOST_PATH/tls.crt"
chmod 0600 "$FRONTEND_TLS_HOST_PATH/tls.key"

export FRONTEND_HTTPS_PORT="${FRONTEND_HTTPS_PORT:-18443}"
export FRONTEND_HTTP_PORT="${FRONTEND_HTTP_PORT:-18088}"

if [ "${DEPLOYMENT_ENV_DIRECT_ACCESS:-1}" != 0 ]; then
  case ":${COMPOSE_FILE:-compose.yml}:" in
    *":tests/acceptance/compose.direct-access.yml:"*) ;;
    *) export COMPOSE_FILE="${COMPOSE_FILE:-compose.yml}:tests/acceptance/compose.direct-access.yml" ;;
  esac
fi

unset _deployment_env_dir
