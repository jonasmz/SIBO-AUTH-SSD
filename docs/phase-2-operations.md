# Phase 2 Operations

Phase 2 adds two services to Compose, `api-a` and `api-b`. Both run the same minimal reference
consumer (`src/ReferenceConsumer.Api`, authorized by Technical Constraints §5.2 amendment 1.1)
and stand in for Business API A and B. Each validates the access tokens issued by `auth-api`
**locally**, with a public key, and never calls Authentication API per request.

## Configuration

Set the variables from `.env.example` (see also `docs/phase-1-operations.md`):

| Variable | Purpose |
|---|---|
| `AUTH_JWT_ISSUER`, `AUTH_JWT_AUDIENCE` | Same values given to `auth-api`; one shared audience is expected by both consumers. |
| `AUTH_JWT_PUBLIC_KEY_HOST_FILE` | Host path of `jwt-public.pem`, mounted read-only into each consumer. |
| `AUTH_JWT_CLOCK_SKEW_SECONDS` | Expiry clock tolerance, 0–60 seconds (reference value 30); identical for both consumers. |
| `API_A_HTTP_PORT`, `API_B_HTTP_PORT` | Host ports (development/acceptance convenience only). |

Derive the public key from the Phase 1 private key:

```bash
openssl pkey -in jwt-private.pem -pubout -out jwt-public.pem
```

## Private key exclusion

Only `jwt-public.pem` is mounted into the consumers. They are never given the RSA key directory
or the SQLite directory, and a consumer refuses to start when the file it is given contains a
private key. Publishing the consumer ports on the host is a development and acceptance
convenience; the production rule that backends are not published directly, and the reverse
proxy, belong to Phase 8.

## Behavior

- `GET /api/caller` requires a valid bearer token and returns `{ service, subject, roles }`.
- `GET /health/live` is anonymous and returns `{"status":"healthy"}`.
- A consumer does not depend on `auth-api` at startup or per request: a token that is still valid
  keeps being accepted while Authentication API is stopped.
- Missing or invalid `Jwt` settings, or an unusable public key, stop the consumer at startup with
  a message that names only the setting.
