# Phase 0 Research: Phase 2 JWT Validation in Consumer APIs

All decisions below apply only to Roadmap Phase 2. They derive from the SRS, Technical
Constraints, Roadmap §8/G2, the constitution, the approved feature specification and its
clarifications, and the explicit project decision recorded below.

## Where Business API A and B Live

**Decision**: Add one minimal reference consumer project, `src/ReferenceConsumer.Api`, to this
repository and deploy it twice in Docker Compose as `api-a` and `api-b`, each with its own
externally supplied service name and the shared validation configuration.

**Rationale**: The real business APIs (CanchaBackend and buffetBackend) are separate repositories
without code. Technical Constraints §5.2 lists only the four `Authentication.*` product projects
and two test projects, so a consumer host here is outside that baseline. The project owner
explicitly chose this option on 2026-10-07 (to be recorded as DEC-009 in the roadmap decision
log during implementation). One project deployed twice proves that two independent services
validate locally with identical policy, without duplicating code. The real business APIs later
adopt the same validation contract.

**Alternatives considered**: Two separate near-identical projects (duplicated code, same
exception required); waiting for the real Cancha/Buffet repositories (Phase 2 could not close in
this repository); test-hosted consumers only (rejected by clarification Q2).

## Consumer Project Shape

**Decision**: The reference consumer is a single ASP.NET Core 10 Minimal API project with one
feature slice (`Features/Caller`) and one security folder for JWT validation setup. It references
no `Authentication.*` project, owns no persistence, and has no Domain/Application/Infrastructure
split.

**Rationale**: It hosts no business behavior; the hexagonal split exists to protect business and
infrastructure boundaries that the consumer does not have. Not referencing Authentication API
projects guarantees by construction that the consumer cannot reach the private key, the SQLite
database, or Identity (FR-003, CR-DATA-004).

**Alternatives considered**: A layered four-project consumer (pure ceremony); referencing
`Authentication.Infrastructure` to reuse option types (couples consumer to the identity
provider and its private-key configuration).

## Validation Library and Settings

**Decision**: Use `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 (authorized by
Technical Constraints §33) with an explicitly constructed `TokenValidationParameters`:
`IssuerSigningKey` = `RsaSecurityKey` from the configured public PEM; `ValidAlgorithms` =
`RS256` only; `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`,
`ValidateIssuerSigningKey`, `RequireSignedTokens`, `RequireExpirationTime` all true;
`ValidIssuer`/`ValidAudience` from configuration; `ClockSkew` from configuration. No
`Authority`/`MetadataAddress` is set, so no OpenID Connect metadata or JWKS is fetched.
`MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`.
`IncludeErrorDetails = false`.

**Rationale**: Microsoft IdentityModel performs all signature and claim validation (Constitution
V). Without an authority the handler never contacts Authentication API (FR-001, FR-011,
NFR-AVAIL-001). Disabling inbound claim mapping keeps the SRS claim names (`sub`, `role`) so the
caller identity maps directly. Hiding error details prevents the `WWW-Authenticate` header from
describing why validation failed (FR-010).

**Alternatives considered**: JWKS/OIDC discovery (excluded scope, DEC-005); custom validation
(prohibited); default claim mapping (renames `sub`/`role` to long URIs and obscures mapping).

## Clock Tolerance

**Decision**: `Jwt:ClockSkewSeconds`, required, externally configured, default reference value
`30`, validated to be between `0` and `60` inclusive. Compose supplies the same value to both
consumers from one variable (`AUTH_JWT_CLOCK_SKEW_SECONDS`).

**Rationale**: The JwtBearer default (5 minutes) would extend a 15-minute token by a third,
violating NFR-TIME-004. Thirty seconds absorbs container clock drift while extending nominal
lifetime by about 3%. One Compose variable makes the value identical across consumers
(NFR-TIME-003, FR-005). The upper bound makes an excessive value a startup failure.

**Alternatives considered**: Zero tolerance (fragile under minor drift); keeping the 5-minute
default (excessive); per-service values (violates consistency).

## Public Key Delivery and Private-Key Exclusion

**Decision**: Each consumer reads one PEM file from `Jwt:PublicKeyPath`. Compose bind-mounts
only the public key file (`AUTH_JWT_PUBLIC_KEY_HOST_FILE`) read-only into each consumer, never
the RSA directory used by `auth-api`. At startup the consumer rejects any PEM whose label is not
`PUBLIC KEY` or `RSA PUBLIC KEY`, so a private key mistakenly supplied causes a startup failure
instead of being held in memory.

**Rationale**: FR-KEY-005 requires external configuration of the public key; mounting a single
file prevents the private key from becoming visible in the consumer container (FR-003,
NFR-DEPLOY-009, TEST-048/049). The label check turns a deployment mistake into a fail-fast error.

**Alternatives considered**: Mounting the shared keys directory (exposes the private key);
inline PEM in an environment variable (harder to restrict and inspect).

## Startup Validation and Diagnostics

**Decision**: Missing/invalid `Jwt:Issuer`, `Jwt:Audience`, `Jwt:PublicKeyPath`,
`Jwt:ClockSkewSeconds`, or an unreadable/unparsable/non-public PEM terminates startup with a
message naming only the setting, following the Phase 1 diagnostics pattern (FR-012).

**Rationale**: Fail-fast configuration is a constitution MUST; naming the setting without its
value is the established Phase 1 convention.

## Endpoints and Authorization

**Decision**: Each consumer exposes:

- `GET /api/caller` — requires an authenticated caller; returns the service name, `sub`, and the
  role list (FR-006, FR-007).
- `GET /api/caller/administrator` — requires the `Administrator` role through a named
  authorization policy; returns the same shape (FR-008).
- `GET /health/live` — anonymous liveness used by Compose demonstrations; it reveals nothing.

Unauthenticated → `401` with `WWW-Authenticate: Bearer` and no body; authenticated without role
→ `403` with no body (FR-009, FR-010).

**Rationale**: Minimal observable surface for every acceptance scenario; authorization uses token
claims and standard ASP.NET Core policies (FR-AUTHZ-005, NFR-003).

**Alternatives considered**: Business-style sample endpoints (out of scope); ProblemDetails
bodies on 401/403 (unnecessary for Phase 2; nothing in the SRS requires them here).

## Authentication API Changes

**Decision**: None to code or contracts. Operational documentation gains the public-key
generation step already used by Phase 1 acceptance (`openssl pkey -pubout`), and Compose gains
the two consumer services plus the shared clock-tolerance and public-key variables.

**Rationale**: The shared audience (clarification Q1) and existing claims already satisfy the
consumers; Phase 1 contracts must remain unchanged (FR-014).

## Testing Strategy

**Decision**: Extend `tests/Authentication.IntegrationTests` (no new test project) with one
consolidated scenario class for the consumer, hosting it through `WebApplicationFactory` twice
(as `api-a` and `api-b`) and, for the end-to-end case, a real Authentication API factory sharing
the same RSA pair. Tokens for rejection and role cases are minted in the test with
`JsonWebTokenHandler` using disposable keys. Expiry cases use `exp` offsets far outside the
tolerance margin relative to the current UTC time, so no sleeps are needed; if the JwtBearer
handler honors the scheme's `TimeProvider`, a controlled `TimeProvider` is used instead. A
`tests/acceptance/phase-2.sh` script demonstrates the Compose behavior, including stopping
`auth-api`, and re-runs `phase-1.sh` for regression.

**Rationale**: Real signed tokens and real JwtBearer validation (NFR-002); minimal additions to
existing infrastructure; Gate G2 requires demonstrating availability independence with real
services.

**Alternatives considered**: Mocking the authentication handler (would not test validation); a
separate consumer test project (no demonstrated need, Constitution IV).

## Consumer Entry Point in Tests

**Decision**: The consumer keeps the compiler-generated internal `Program`; it exposes a public
marker type (`ReferenceConsumerEntryPoint`) used as the `WebApplicationFactory<T>` type argument.

**Rationale**: The integration test project already references Authentication API's public
`Program`; a second public global `Program` would be ambiguous.

## Container Image

**Decision**: `src/ReferenceConsumer.Api/Dockerfile` mirrors the Phase 1 multi-stage, non-root
image using the authorized `sdk:10.0` and `aspnet:10.0` development tags; it copies only the
consumer project and shared build files.

**Rationale**: Consistency with Technical Constraints §28; the consumer image contains no
Authentication API code or secrets.
