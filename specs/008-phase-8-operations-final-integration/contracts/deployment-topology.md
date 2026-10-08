# Contract: Production Deployment Topology

## Services (`compose.yml`, exactly four)

| Service | Image | Published ports | Mounts (host → container) | Network |
|---|---|---|---|---|
| `frontend` | official `nginx` stable Alpine, pinned digest | `${FRONTEND_HTTPS_PORT:-443}:443`, `${FRONTEND_HTTP_PORT:-80}:80` | `deploy/frontend/nginx.conf` → `/etc/nginx/conf.d/default.conf` (ro); `${FRONTEND_STATIC_HOST_PATH}` → `/usr/share/nginx/html` (ro); `${FRONTEND_TLS_HOST_PATH}` → `/etc/nginx/tls` (ro) | default, fixed `${FRONTEND_INTERNAL_ADDRESS}` |
| `auth-api` | built from `src/Authentication.Api/Dockerfile` | none | `${AUTH_SQLITE_HOST_PATH}` → `/var/lib/auth-api/data`; `${AUTH_DATAPROTECTION_HOST_PATH}` → `/var/lib/auth-api/dataprotection`; `${AUTH_RSA_HOST_PATH}` → `/var/lib/auth-api/keys` (ro); `${AUTH_LOGS_HOST_PATH}` → `/app/logs` | default |
| `api-a`, `api-b` | built from `src/ReferenceConsumer.Api/Dockerfile` | none | `${AUTH_JWT_PUBLIC_KEY_HOST_FILE}` → `/var/lib/consumer/jwt-public.pem` (ro, single file) | default |

Default network subnet `${AUTH_INTERNAL_SUBNET:-172.30.80.0/24}`; frontend address
`${FRONTEND_INTERNAL_ADDRESS:-172.30.80.10}`; auth-api `ReverseProxy__TrustedProxies`
= `${AUTH_TRUSTED_PROXIES:-172.30.80.10}`. All .NET services run with
`ASPNETCORE_ENVIRONMENT=Production`. No other service, volume-only critical storage, or init step.

## Routing (frontend, single external origin)

| Browser path | Upstream | Notes |
|---|---|---|
| `/` and any other path | static files, SPA fallback `index.html` | operator-supplied compiled Angular files |
| `/auth/api/...` | `http://auth-api:8080/api/...` | prefix stripped; first limiting layer on `/auth/api/auth/{login,refresh,forgot-password,reset-password}`; `proxy_cookie_path /api/auth /auth/api/auth` |
| `/api-a/api/...` | `http://api-a:8080/api/...` | prefix stripped |
| `/api-b/api/...` | `http://api-b:8080/api/...` | prefix stripped |
| any other `/auth/...`, `/api-a/...`, `/api-b/...` | `404` | health, OpenAPI, Scalar never routed |
| `http://...` (port 80) | `301` to `https://` | |

Headers set (overwriting any client value) on every proxied request: `Host $host`,
`X-Forwarded-For $remote_addr`, `X-Forwarded-Proto $scheme`. `client_max_body_size 16k`.
First layer: `limit_req_zone $binary_remote_addr` 60 r/min, burst 20, `limit_req_status 429`.

## Cookie contract through the entry point

auth-api sets `auth_refresh` with `Path=/api/auth`; the browser receives `Path=/auth/api/auth` and
returns it on `POST /auth/api/auth/refresh` and `POST /auth/api/auth/logout`. `HttpOnly`,
`Secure`, `SameSite=Strict` unchanged. `Security__FrontendOrigin` = the external `https://` origin.

## Required operator inputs

| Variable | Content | Ownership / mode (reference) |
|---|---|---|
| `AUTH_SQLITE_HOST_PATH` | directory for `auth.db` | owned by container UID (`APP_UID`), `0700` |
| `AUTH_DATAPROTECTION_HOST_PATH` | key ring directory | container UID, `0700` |
| `AUTH_RSA_HOST_PATH` | `jwt-private.pem` (+ `jwt-public.pem`) | private key readable only by container UID, `0400`/`0600`; dir `0700` |
| `AUTH_JWT_PUBLIC_KEY_HOST_FILE` | public key file | world-readable `0644` |
| `AUTH_LOGS_HOST_PATH` | log directory | container UID, `0750` |
| `FRONTEND_STATIC_HOST_PATH` | compiled Angular files | readable by Nginx |
| `FRONTEND_TLS_HOST_PATH` | `tls.crt`, `tls.key` | key readable by Nginx only, `0600` |

Deleting persistent data requires removing these host paths explicitly; no Compose command does it.

## Acceptance-only overrides (never used in production)

- `tests/acceptance/compose.direct-access.yml`: publishes `auth-api` `${AUTH_HTTP_PORT}:8080`,
  `api-a` `${API_A_HTTP_PORT}:8080`, `api-b` `${API_B_HTTP_PORT}:8080` for Phase 1–7 procedures.
- `tests/acceptance/compose.mail-sink.yml` (Phase 6), `tests/acceptance/compose.reference-proxy.yml` (Phase 7).
