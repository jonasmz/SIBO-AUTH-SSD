# Feature Specification: Phase 2 — JWT Validation in Consumer APIs

**Feature Branch**: `002-jwt-validation-consumer-apis`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Phase 2 — JWT validation in consumer APIs

## Clarifications

### Session 2026-10-07

- Q: Do tokens carry one shared audience validated by both business APIs, or does each API have its own? → A: One shared audience, configured identically in Authentication API, Business API A, and Business API B; the same token is valid on both.
- Q: Are Business API A and B runnable Compose services in this phase, or only hosted inside automated tests? → A: Runnable Compose services (no reverse proxy or frontend); the acceptance demonstration stops Authentication API and shows a still-valid token is accepted.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Business APIs Accept Valid Tokens Locally (Priority: P1)

A client that holds a valid access token issued by Authentication API calls a protected
endpoint of Business API A and of Business API B. Each API verifies the token on its own, using
only public verification material, and identifies the caller by stable user identifier and role,
without contacting Authentication API for the request.

**Why this priority**: This proves that centralized authentication works across services, which
is the purpose of the phase and the foundation for every later protected capability.

**Independent Test**: Obtain a token from the Phase 1 login flow, call one protected endpoint in
each business API with it, and confirm both succeed and report the caller's stable identifier and
roles. Repeat while Authentication API is stopped and confirm the still-valid token is accepted.

**Acceptance Scenarios**:

1. **Given** a valid, unexpired token signed by Authentication API, **When** it is presented to a
   protected endpoint of Business API A, **Then** the request is authorized and the API identifies
   the caller by the token's stable user identifier and roles.
2. **Given** the same token, **When** it is presented to a protected endpoint of Business API B,
   **Then** the request is authorized with the same caller identity.
3. **Given** Authentication API is temporarily unavailable, **When** a still-valid token is
   presented to either business API, **Then** the request is authorized exactly as when
   Authentication API is available.
4. **Given** either business API is configured and running, **When** its configuration and
   runtime assets are inspected, **Then** only public verification material is present and no
   private signing key is available to it.

---

### User Story 2 - Business APIs Reject Invalid Tokens (Priority: P2)

A request that presents no token, or a token that is forged, expired, or intended for a different
issuer or audience, is rejected by both business APIs before reaching protected behavior.

**Why this priority**: Accepting valid tokens is only safe if every invalid token is refused;
this protects the business APIs from forged or misdirected credentials.

**Independent Test**: Against each business API, call a protected endpoint with no token, a token
signed with an unrelated key, an expired token, a token with a different issuer, and a token with
a different audience, and confirm each is answered with `401 Unauthorized`.

**Acceptance Scenarios**:

1. **Given** a protected endpoint, **When** a request carries no token, **Then** the API
   responds `401 Unauthorized`.
2. **Given** a token whose signature was not produced by the Authentication API signing key,
   **When** it is presented, **Then** the API responds `401 Unauthorized`.
3. **Given** a token whose expiry has passed beyond the configured clock tolerance, **When** it
   is presented, **Then** the API responds `401 Unauthorized`.
4. **Given** a token with an unexpected issuer, or one whose audience differs from the shared
   audience, **When** it is presented, **Then** the API responds `401 Unauthorized`.
5. **Given** a token using a signing algorithm other than the expected one, or an unsigned
   token, **When** it is presented, **Then** the API responds `401 Unauthorized`.
6. **Given** any rejected request, **When** the response is examined, **Then** it discloses no
   token contents, keys, or internal validation detail.

---

### User Story 3 - Role-Based Access Is Enforced (Priority: P3)

A caller with a valid token but without the role a protected endpoint requires is refused with a
distinct "forbidden" outcome, so clients can tell a missing or bad credential from insufficient
privilege.

**Why this priority**: It completes the minimal authorization model that later administrative
phases rely on, using the role information carried in the token.

**Independent Test**: In each business API, call an endpoint that requires the `Administrator`
role with (a) a valid token carrying that role and (b) a valid token carrying a different role,
and confirm the first succeeds and the second is answered with `403 Forbidden`.

**Acceptance Scenarios**:

1. **Given** a valid token carrying the `Administrator` role, **When** it is presented to a
   role-restricted endpoint of either business API, **Then** the request is authorized.
2. **Given** a valid token that does not carry the required role, **When** it is presented to
   that endpoint, **Then** the API responds `403 Forbidden`, not `401 Unauthorized`.
3. **Given** a request with no valid token, **When** it targets the role-restricted endpoint,
   **Then** the API responds `401 Unauthorized`, not `403 Forbidden`.

### Edge Cases

- A token expired by less than the configured clock tolerance is handled according to that single
  explicit tolerance; a token expired by more than it is always rejected.
- A token carrying multiple roles, or a single role, yields the complete set of roles for
  authorization decisions.
- A token carrying no role claim authenticates the caller but is refused on role-restricted
  endpoints with `403 Forbidden`.
- Missing or unreadable public verification material, or missing issuer or audience
  configuration, prevents the business API from starting rather than silently accepting or
  rejecting everything.
- A malformed token value, or an authorization header that is not a bearer credential, is treated
  as an unauthenticated request (`401 Unauthorized`).
- Rotation, revocation, and remote introspection of tokens are not available; a token that is
  otherwise valid remains accepted until it expires.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Business API A and Business API B MUST each validate the access tokens presented to
  them locally, using the public verification material of the Authentication API signing key and
  without contacting Authentication API for ordinary requests.
- **FR-002**: Each business API MUST verify, for every presented token, the signature, the
  expected signing algorithm, the issuer, the audience, and the expiry; failure of any check
  MUST result in `401 Unauthorized`.
- **FR-003**: Each business API MUST be supplied only the public verification material; the
  private signing key MUST NOT be present in, mounted into, or configured for either business API.
- **FR-004**: The expected issuer, the expected audience (one value shared by Authentication API
  and both business APIs), and the public verification material MUST be supplied to each business API through external configuration, not embedded in source or
  in a container image.
- **FR-005**: The clock tolerance applied when evaluating token expiry MUST be explicit,
  externally configurable, identical in both business APIs, and short enough not to materially
  extend the nominal token lifetime.
- **FR-006**: Each business API MUST establish the caller's identity from the token's stable user
  identifier and make the caller's roles available for authorization.
- **FR-007**: Each business API MUST expose at least one endpoint that requires an authenticated
  caller and reports the caller's stable user identifier and roles, so identity mapping is
  observable.
- **FR-008**: Each business API MUST expose at least one endpoint that requires the
  `Administrator` role.
- **FR-009**: A request without valid authentication MUST receive `401 Unauthorized`; an
  authenticated request lacking the required role MUST receive `403 Forbidden`. The two outcomes
  MUST be distinguishable.
- **FR-010**: Rejection responses MUST NOT disclose token contents, key material, configuration
  values, stack traces, or internal validation detail.
- **FR-011**: A token that is valid when presented MUST continue to be accepted while
  Authentication API is unavailable.
- **FR-012**: Each business API MUST fail to start, without exposing secrets, when its required
  verification configuration (public key, issuer, audience, clock tolerance) is missing or
  invalid.
- **FR-013**: Business API A and Business API B MUST each run as a service of the Docker Compose
  reference deployment, configured only through external configuration and public verification
  material, and the reference deployment MUST still contain no migration or bootstrap service
  and no reverse proxy or frontend.
- **FR-014**: Phase 1 behavior — startup, login, token issuance, health, and persistence — MUST
  remain unchanged and verified.

### Applicable Non-Functional Requirements

- **NFR-001**: Validation MUST rely on UTC time for token expiry evaluation.
- **NFR-002**: Phase 2 verification MUST use a small, high-value set of automated tests that
  exercise current and previous capabilities only, using real signed tokens rather than
  simulated validation, and MUST NOT rely on future-phase capabilities or placeholder tests.
- **NFR-003**: Authorization decisions MUST be made from claims carried in the presented token
  and standard platform authorization policies, not from calls to Authentication API.

### Key Entities *(include if feature involves data)*

- **Consumer API**: A business API (A or B) that receives access tokens, validates them locally,
  and authorizes requests by the caller's identity and roles.
- **Public verification material**: The public half of the Authentication API signing key pair,
  distributed to each consumer API through external configuration; it cannot sign tokens.
- **Validated caller identity**: The stable user identifier and role set derived from a
  successfully validated token and used for authorization.
- **Token validation policy**: The expected signing algorithm, issuer, audience, and clock
  tolerance that every consumer API applies identically.

### Scope Exclusions

This feature MUST NOT introduce:

- Refresh tokens, renewable sessions, logout, or session revocation.
- Administrative user or role management, or dynamic roles.
- Token revocation, blacklists, or remote introspection or validation through Authentication API.
- JWKS publication or automatic signing-key rotation.
- Business capabilities beyond the minimal protected endpoints needed to demonstrate validation.
- Reverse-proxy routing, frontend integration, final four-service deployment acceptance,
  backup/restore, or hardening assigned to later phases (the two business API services are added
  to Compose, but no proxy, frontend, or later-phase service).
- Any change to Authentication API behavior, contracts, or persistence established in Phase 1.
- Speculative abstractions, shared libraries, or persistence intended solely for later phases.

## Traceability

| Feature area | Normative sources |
|---|---|
| Local validation by consumers | SRS FR-JWT-010 through FR-JWT-012; Roadmap §8.2 |
| Public material only; no private key | SRS FR-JWT-003, FR-JWT-004; NFR-DEPLOY-008, NFR-DEPLOY-009; TEST-018, TEST-019, TEST-048, TEST-049 |
| Rejection cases | SRS TEST-015 through TEST-017; Roadmap §8.4 |
| Clock tolerance | SRS NFR-TIME-001 through NFR-TIME-004 |
| `401` vs `403`, role claims | SRS FR-AUTHZ-003 through FR-AUTHZ-005; Roadmap §8.2 |
| Phase boundary and verification | Constitution Principles I, II, VI, and VII; Roadmap RD-001 through RD-008, §8.3 through §8.5 |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A token issued by Authentication API is accepted by a protected endpoint of each
  of the two business APIs, and each reports the same stable user identifier and roles as the
  token.
- **SC-002**: With Authentication API stopped in the Compose deployment, a still-valid token
  continues to be accepted by both business APIs.
- **SC-003**: 100% of tested invalid-token cases — no token, forged signature, expired,
  wrong issuer, wrong audience, unexpected algorithm — are answered `401 Unauthorized` by both
  business APIs, with no token or key detail in the response.
- **SC-004**: A valid token lacking the required role is answered `403 Forbidden` and a request
  without valid authentication is answered `401 Unauthorized`, on both business APIs.
- **SC-005**: Inspection shows that neither business API has access to the private signing key.
- **SC-006**: Gate G2 evidence shows build, focused automated verification, the validation
  workflow, and Phase 1 regression checks passing before Phase 2 is marked complete.

## Assumptions

- Phase 1 is complete: Authentication API issues RS256 access tokens containing the stable user
  identifier, email, role, issuer, audience, issue time, expiry, and token identifier.
- Business API A and Business API B are new services created in this phase solely to host the
  protected endpoints that demonstrate validation; they carry no business capability yet.
- The only role in existence is `Administrator`; "insufficient role" scenarios use validly signed
  tokens carrying a different role, produced with a disposable test key pair.
- Authentication API issues a single audience value, unchanged from Phase 1; both business APIs
  expect that same value, so a single token is valid on both. Per-API audiences are not
  introduced.
- A short clock tolerance, on the order of seconds and far below the 15-minute token lifetime,
  is used; the exact value is chosen during planning and applied identically to both APIs.
- The public verification material is delivered to the business APIs through operator-supplied
  external configuration; manual replacement of that configuration is the only key-change
  mechanism.
- The final four-service Compose topology, reverse-proxy routing, and final acceptance remain
  Phase 8 work; this phase adds only the two business API services to Compose, each reachable
  directly for demonstration and verification.
