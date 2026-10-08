# Data Model: Phase 2 JWT Validation in Consumer APIs

## Modeling Boundary

Phase 2 persists nothing. The reference consumer owns no database and reads no Authentication
API storage (CR-DATA-004). All models below are transient or configuration.

## Configuration: Token Validation Policy

Bound from external configuration in each consumer instance; identical in `api-a` and `api-b`
except `Service:Name`.

| Setting | Rule |
|---|---|
| `Service:Name` | Required, nonblank; identifies the instance (`api-a` / `api-b`) in responses. Not a security input. |
| `Jwt:Issuer` | Required, nonblank; must equal the issuer configured in Authentication API. |
| `Jwt:Audience` | Required, nonblank; the single shared audience (clarification Q1). |
| `Jwt:PublicKeyPath` | Required; readable file containing a PEM labelled `PUBLIC KEY` or `RSA PUBLIC KEY`. A private-key PEM is rejected. |
| `Jwt:ClockSkewSeconds` | Required integer, `0`–`60`; reference value `30`; same value in both consumers. |

Invalid or missing values terminate startup with a message naming only the setting.

Fixed (not configurable): accepted algorithm `RS256`; signed tokens and `exp` required; claim
names `sub` (name) and `role` (roles); no metadata/JWKS retrieval.

## Transient: Presented Access Token

Issued by Authentication API (Phase 1 contract, unchanged).

| Claim | Consumer use |
|---|---|
| `sub` | Stable caller identifier. |
| `email` | Not used for authorization. |
| `role` | Zero, one, or many; forms the caller role set. |
| `iss` | Must equal `Jwt:Issuer`. |
| `aud` | Must equal `Jwt:Audience`. |
| `iat` | Informational. |
| `exp` | Must be later than now minus `Jwt:ClockSkewSeconds`. |
| `jti` | Not used in Phase 2 (no revocation). |

## Transient: Validated Caller Identity

| Field | Rule |
|---|---|
| `Subject` | Value of `sub`. |
| `Roles` | All `role` claim values; empty when absent. |

Returned by the caller endpoints together with `Service:Name`.

## Request Outcomes

```text
no / malformed / non-bearer credential ─┐
bad signature / wrong alg / unsigned ───┤
wrong iss / wrong aud ──────────────────┼──> 401 Unauthorized (no body, no error detail)
expired beyond tolerance ───────────────┘
valid token ──> /api/caller ──────────────────────────────> 200 caller identity
valid token ──> /api/caller/administrator ── has role ────> 200 caller identity
                                          └─ lacks role ──> 403 Forbidden (no body)
```

## External Operational State

| Artifact | Rule |
|---|---|
| Public PEM | Derived from the Phase 1 private key; mounted read-only as a single file into each consumer; carries no signing capability. |
| Private PEM | Unchanged Phase 1 asset; mounted only into `auth-api`; never into a consumer. |

## Explicitly Absent from Phase 2

- Any consumer database, cache, or business entity
- Token revocation, blacklist, introspection, or JWKS documents
- Refresh tokens, sessions, or cookies
- Per-API audiences or multiple signing keys
