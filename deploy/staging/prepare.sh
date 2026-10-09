#!/usr/bin/env bash
# Prepares a persistent staging environment on this host (idempotent: existing keys, certificate, data and
# environment file are never overwritten). Creates the host directories with the ownership and modes of
# specs/008-phase-8-operations-final-integration/contracts/deployment-topology.md, an RSA key pair, a
# self-signed TLS certificate, the temporary frontend, and an environment file.
# Usage: deploy/staging/prepare.sh [ROOT] [PUBLIC_HOST]   (defaults: ./.staging  localhost)
# Requires: docker, openssl. Then start with the command it prints.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ROOT="$(realpath -m "${1:-$REPO_ROOT/.staging}")"
HOST="${2:-localhost}"
HTTPS_PORT="${STAGING_HTTPS_PORT:-8443}"
HTTP_PORT="${STAGING_HTTP_PORT:-8080}"
RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:10.0"

# Operations that need the container user's ownership run in a throwaway root container.
as_root() { docker run --rm --user root --entrypoint sh -v "$ROOT:/staging" "$RUNTIME_IMAGE" -c "$1"; }
APP_UID="$(docker run --rm --entrypoint sh "$RUNTIME_IMAGE" -c 'echo $APP_UID')"

mkdir -p "$ROOT"/{data,dataprotection,keys,logs,public,tls,frontend}

if [ ! -f "$ROOT/keys/jwt-private.pem" ]; then
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$ROOT/keys/jwt-private.pem" 2>/dev/null
  openssl pkey -in "$ROOT/keys/jwt-private.pem" -pubout -out "$ROOT/public/jwt-public.pem"
  chmod 0644 "$ROOT/public/jwt-public.pem"
fi

if [ ! -f "$ROOT/tls/tls.crt" ]; then
  san="DNS:localhost,IP:127.0.0.1"
  if [[ "$HOST" =~ ^[0-9.]+$ ]]; then san="$san,IP:$HOST"; elif [ "$HOST" != localhost ]; then san="$san,DNS:$HOST"; fi
  openssl req -x509 -newkey rsa:2048 -nodes -days 365 -subj "/CN=$HOST" -addext "subjectAltName=$san" \
    -keyout "$ROOT/tls/tls.key" -out "$ROOT/tls/tls.crt" 2>/dev/null
  chmod 0644 "$ROOT/tls/tls.crt"; chmod 0600 "$ROOT/tls/tls.key"   # Nginx's master process reads the key as root
  chmod 0755 "$ROOT/tls"
fi

# Temporary frontend (replace the contents with the compiled Angular application when it exists).
cp -r "$REPO_ROOT/deploy/frontend-stopgap/." "$ROOT/frontend/"
chmod -R a+rX "$ROOT/frontend"

as_root "chown -R $APP_UID:$APP_UID /staging/data /staging/dataprotection /staging/logs /staging/keys \
  && chmod 0700 /staging/data /staging/dataprotection /staging/keys && chmod 0750 /staging/logs \
  && chmod 0600 /staging/keys/jwt-private.pem"

if [ ! -f "$ROOT/staging.env" ]; then
  umask 077
  cat > "$ROOT/staging.env" <<ENV
COMPOSE_PROJECT_NAME=auth-staging
AUTH_JWT_ISSUER=https://$HOST:$HTTPS_PORT
AUTH_JWT_AUDIENCE=authentication-clients
AUTH_FRONTEND_ORIGIN=https://$HOST:$HTTPS_PORT
AUTH_SQLITE_HOST_PATH=$ROOT/data
AUTH_DATAPROTECTION_HOST_PATH=$ROOT/dataprotection
AUTH_RSA_HOST_PATH=$ROOT/keys
AUTH_JWT_PUBLIC_KEY_HOST_FILE=$ROOT/public/jwt-public.pem
AUTH_LOGS_HOST_PATH=$ROOT/logs
FRONTEND_STATIC_HOST_PATH=$ROOT/frontend
FRONTEND_TLS_HOST_PATH=$ROOT/tls
FRONTEND_HTTPS_PORT=$HTTPS_PORT
FRONTEND_HTTP_PORT=$HTTP_PORT
AUTH_SMTP_HOST=mail-sink
AUTH_SMTP_PORT=1025
AUTH_SMTP_SECURITY=None
AUTH_SMTP_USERNAME=mail-user
AUTH_SMTP_PASSWORD=mail-password
AUTH_SMTP_SENDER_ADDRESS=no-reply@staging.invalid
AUTH_SMTP_SENDER_NAME=Authentication Staging
AUTH_LOG_RETENTION_DAYS=30
AUTH_INTERNAL_SUBNET=172.30.90.0/24
FRONTEND_INTERNAL_ADDRESS=172.30.90.10
STAGING_MAILBOX_PORT=8025
ENV
fi

cat <<MSG
Staging prepared in $ROOT

Start:   docker compose --env-file $ROOT/staging.env -f compose.yml -f deploy/staging/compose.mailpit.yml up --build -d
Open:    https://$HOST:$HTTPS_PORT   (self-signed certificate: accept the browser warning)
Inbox:   http://127.0.0.1:8025       (password-recovery emails; loopback only)
Admin:   admin@local.invalid / admin  -> change this password immediately
Stop:    docker compose --env-file $ROOT/staging.env -f compose.yml -f deploy/staging/compose.mailpit.yml down
Data survives 'down -v'; to reset, remove $ROOT/data explicitly (as root: files belong to uid $APP_UID).
MSG
