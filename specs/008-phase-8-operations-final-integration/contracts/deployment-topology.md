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
| `/` and any path outside the prefixes below | static files, SPA fallback `index.html` | operator-supplied compiled Angular files |
| `/auth/admin/<resource>` | `http://auth-api:8080/api/admin/<resource>` | all user and role administration routes, path parameters and sub-resources kept; `Location` headers translated `/api/admin/` → `/auth/admin/` |
| `/auth/login`, `/auth/refresh`, `/auth/forgot-password`, `/auth/reset-password` | `http://auth-api:8080/api/auth/<same>` | first limiting layer applies |
| `/auth/<operation>` (`logout`, `change-password`) | `http://auth-api:8080/api/auth/<operation>` | |
| `/api-a/api/...` | `http://api-a:8080/api/...` | existing prefix kept, prefix stripped |
| `/api-b/api/...` | `http://api-b:8080/api/...` | existing prefix kept, prefix stripped |
| any other `/api-a/...`, `/api-b/...` | `404` | consumer health never routed |
| `http://...` (port 80) | `301` to `https://` | |

### Public Authentication API URL contract

| Public URL | Internal route (unchanged) |
|---|---|
| `POST /auth/login` | `POST /api/auth/login` |
| `POST /auth/refresh` | `POST /api/auth/refresh` |
| `POST /auth/logout` | `POST /api/auth/logout` |
| `POST /auth/change-password` | `POST /api/auth/change-password` |
| `POST /auth/forgot-password` | `POST /api/auth/forgot-password` |
| `POST /auth/reset-password` | `POST /api/auth/reset-password` |
| `GET/POST /auth/admin/users`, `GET/PATCH /auth/admin/users/{id}`, `PUT /auth/admin/users/{id}/roles`, `POST /auth/admin/users/{id}/{enable,disable,revoke-sessions}` | the same paths under `/api/admin/` |
| `GET/POST /auth/admin/roles`, `PATCH/DELETE /auth/admin/roles/{id}` | the same paths under `/api/admin/` |

Translation rule: `/auth/admin/X` → `/api/admin/X`; otherwise `/auth/X` → `/api/auth/X`. Because the
generic rule maps into `/api/auth/`, redundant or internal forms resolve to routes that do not exist
and receive `404`: `/auth/api/auth/login` → `/api/auth/api/auth/login`, `/auth/health/ready` →
`/api/auth/health/ready`, `/auth/openapi/v1.json`, `/auth/scalar`. Health, OpenAPI, and Scalar are
therefore never reachable through the entry point.

Reference Nginx shape (the implementation may differ in form, not in behavior):

```nginx
proxy_cookie_path /api/auth /auth;                       # cookie Path=/api/auth -> Path=/auth
location ^~ /auth/admin/ {
    proxy_pass     http://auth-api:8080/api/admin/;
    proxy_redirect /api/admin/ /auth/admin/;            # Location of created resources
}
location ~ ^/auth/(login|refresh|forgot-password|reset-password)$ {
    limit_req zone=auth_sensitive burst=20 nodelay;
    rewrite ^/auth/(.*)$ /api/auth/$1 break;
    proxy_pass http://auth-api:8080;
}
location /auth/ { proxy_pass http://auth-api:8080/api/auth/; }
```

Implementation notes found by acceptance: `absolute_redirect off` keeps the translated `Location`
relative (`/auth/admin/users/{id}`), as the application issues it, instead of an absolute URL built from
Nginx's own name and listen port; exact `/api-a/api` and `/api-b/api` answer `404` instead of the
trailing-slash redirect Nginx would otherwise issue.

Headers set (overwriting any client value) on every proxied request: `Host $host`,
`X-Forwarded-For $remote_addr`, `X-Forwarded-Proto $scheme`. `client_max_body_size 16k`.
First layer: `limit_req_zone $binary_remote_addr` 60 r/min, burst 20, `limit_req_status 429`.

## Cookie contract through the entry point

auth-api keeps issuing `auth_refresh` with `Path=/api/auth` (unchanged Phase 4 behavior). The proxy
rewrites only the path, so the browser receives `Path=/auth` on `/auth/login` and `/auth/refresh`, and
the clearing cookie of `/auth/logout` also carries `Path=/auth` so it replaces the stored one. Name,
value, `HttpOnly`, `Secure`, `SameSite=Strict`, `Expires`/`Max-Age` are not changed. The browser
returns the cookie on `POST /auth/refresh` and `POST /auth/logout` (and, harmlessly, on
`/auth/admin/*`, which ignores it). The Origin check is unchanged: `Security__FrontendOrigin` is the
external `https://` origin, and refresh/logout from any other origin are still refused before the
cookie is read. auth-api trusts forwarded headers only from the frontend's fixed address.

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
