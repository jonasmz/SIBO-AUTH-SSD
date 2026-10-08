# Feature Specification: Phase 4 — Refresh Tokens, Renewable Sessions and Logout

**Feature Branch**: `004-phase-4-refresh-sessions-logout`  
**Created**: 2026-10-08  
**Status**: Draft  
**Input**: Phase 4 — Refresh Tokens, Renewable Sessions and Logout

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Renewable Login Session (Priority: P1)

An enabled user who successfully signs in receives the established access-token response and a
secure renewable browser session, allowing access to be renewed without sending credentials again.

**Why this priority**: This creates the renewable-session capability on which all other Phase 4
flows depend, while retaining the established login and access-token contract.

**Independent Test**: Sign in with valid credentials, verify the established access-token response
and expiry information, and verify that a browser-only refresh credential establishes a renewable
session without being exposed to browser script.

**Acceptance Scenarios**:

1. **Given** an enabled, non-locked user with valid credentials, **When** the user signs in through
   `POST /api/auth/login`, **Then** the response retains the established access token and expiration
   information and establishes one new renewable session in a new token family.
2. **Given** a successful browser sign-in, **When** the refresh credential is delivered, **Then** it
   is delivered only in an HttpOnly cookie, uses `SameSite=Strict` in the defined same-origin
   deployment, and is marked Secure in production.
3. **Given** invalid credentials, a disabled account, or an Identity-locked account, **When** login
   is attempted, **Then** it preserves the established generic credential-failure behavior and
   creates no renewable session.

---

### User Story 2 - Refresh Token Rotation (Priority: P1)

A user with a valid renewable browser session obtains a current access token and a replacement
refresh credential without needing a currently valid access token.

**Why this priority**: Rotation is the normal renewal path and limits the usefulness of a copied
refresh credential while preserving the stateless access-token model.

**Independent Test**: Sign in, call `POST /api/auth/refresh` with the issued browser credential,
then verify a new access token and replacement cookie are issued and the former credential no
longer works.

**Acceptance Scenarios**:

1. **Given** a valid, unexpired, unrevoked refresh credential for an enabled and non-locked user,
   **When** `POST /api/auth/refresh` is called from the configured frontend origin, **Then** it
   returns a newly issued access token and its expiration information, invalidates the presented
   credential, and updates the refresh cookie with its replacement in the same family.
2. **Given** an expired, unknown, revoked, malformed, or otherwise unusable refresh credential,
   **When** refresh is requested, **Then** no credential is issued and the endpoint returns
   `401 Unauthorized` without identifying the internal token state.
3. **Given** a disabled or currently Identity-locked user with a refresh credential, **When**
   refresh is requested, **Then** no access or replacement refresh credential is issued and the
   request is rejected as invalid refresh credentials.

---

### User Story 3 - Replay Detection Protects a Session Family (Priority: P1)

The system detects reuse of a refresh credential that has already been rotated and terminates the
whole affected renewable-session family.

**Why this priority**: Family-wide revocation contains refresh-token replay, the central security
property of renewable sessions.

**Independent Test**: Sign in, refresh once, submit the original credential again, and verify the
replacement credential and every other continuation in that family can no longer renew.

**Acceptance Scenarios**:

1. **Given** a refresh credential successfully consumed by a prior refresh, **When** it is presented
   again, **Then** the request is rejected as invalid refresh credentials, the associated family is
   revoked, and a security event is recorded without including raw token material.
2. **Given** simultaneous refresh requests using the same valid credential, **When** they contend to
   consume it, **Then** at most one request succeeds and establishes a continuation; the other does
   not issue credentials and receives the replay-safe invalid-refresh outcome.
3. **Given** a family revoked because of replay, **When** its replacement credential is later
   presented, **Then** it cannot renew the session.

---

### User Story 4 - Logout Ends the Current Renewable Session (Priority: P2)

A user can end the renewable session represented by the browser refresh credential without trying
to centrally revoke access tokens already issued.

**Why this priority**: Logout gives users a predictable way to stop future renewal while retaining
the established expiry-based access-token model.

**Independent Test**: Sign in, log out, retry logout, and attempt refresh with the original
credential; confirm both logout calls are safe and renewal is impossible afterward.

**Acceptance Scenarios**:

1. **Given** a browser holds an active refresh credential, **When** it calls `POST /api/auth/logout`
   from the configured frontend origin, **Then** the corresponding renewable session or family is
   revoked, the browser cookie is invalidated, and further refresh through it is prevented.
2. **Given** the same browser calls logout again, or no usable refresh credential is present,
   **When** logout is requested, **Then** it remains a successful idempotent client operation and
   the refresh cookie is invalidated or cleared.
3. **Given** an access token issued before logout, **When** it is presented before its natural
   expiration, **Then** it remains subject to the unchanged local JWT validation rules rather than
   a central blacklist.

---

### User Story 5 - Administrators Revoke User Sessions (Priority: P2)

An administrator can revoke all renewable sessions for a specified user, including families that
were created through separate successful logins.

**Why this priority**: This permits administrative containment of compromised or unwanted browser
sessions without broadening administration into session browsing or dashboards.

**Independent Test**: Establish two renewable sessions for one user, call
`POST /api/admin/users/{id}/revoke-sessions` with an Administrator token, and verify neither
credential can refresh; repeat with missing and non-administrator access tokens.

**Acceptance Scenarios**:

1. **Given** a valid Administrator access token and a user with active renewable-session families,
   **When** the administrator calls `POST /api/admin/users/{id}/revoke-sessions`, **Then** every
   active family for that user is revoked and cannot subsequently refresh.
2. **Given** a request without valid authentication or without the Administrator role, **When** the
   administrative revocation endpoint is called, **Then** it preserves the established `401` and
   `403` authorization conventions.
3. **Given** an unknown user identifier, **When** the endpoint is called by an administrator,
   **Then** it preserves the established `404 Not Found` convention.

---

### User Story 6 - Disabling an Account Ends Renewable Sessions (Priority: P2)

When an administrator disables an account, the user loses both the ability to sign in and every
ability to renew existing sessions; enabling the account later does not restore those sessions.

**Why this priority**: Disabling is the existing administrative withdrawal-of-access control, now
extended to the persistent session state introduced in this phase.

**Independent Test**: Establish one or more renewable sessions, disable the user, verify their
credentials cannot refresh, re-enable the user, and verify old credentials remain unusable while a
new successful login may establish a new session.

**Acceptance Scenarios**:

1. **Given** a user with active renewable-session families, **When** an authorized administrator
   disables the user through the existing disable operation, **Then** all those families are
   revoked as part of that outcome and none can produce new credentials.
2. **Given** a disabled user later enabled through the existing enable operation, **When** a prior
   refresh credential is presented, **Then** it remains unusable; **When** the user signs in
   successfully under the existing Identity rules, **Then** a new family may be created.
3. **Given** an attempt to disable the last enabled Administrator, **When** the existing Phase 3
   protection refuses it, **Then** the user remains enabled and their renewable sessions are not
   revoked by that refused operation.

### Edge Cases

- A refresh credential that has reached absolute expiration, or cannot be verified, never creates
  credentials and receives the same non-revealing `401 Unauthorized` outcome as other unusable
  refresh credentials.
- A rotation, logout, user disable, and administrator family revocation that overlap must leave no
  path for a revoked family to renew. A concurrent refresh has only one possible successful
  consumer; a competing use must not create an independent valid continuation.
- Replaying a credential known to be previously rotated is distinguished internally for security
  event recording and family revocation, but not distinguished in the HTTP response.
- Logout without a usable browser cookie is idempotent and must still remove or invalidate the
  cookie at the browser boundary.
- A user may have multiple independently created families. Logout affects the family represented
  by its refresh credential; administrative revocation and disabling affect all active families
  for that user.
- A family revoked by replay, logout, disable, or administrative revocation stays revoked after an
  application restart and is never restored by account enablement.
- Access tokens issued before rotation, replay detection, logout, disable, or administrative
  revocation retain their normal cryptographic validity until expiry; Business API A and Business
  API B continue local validation and do not query session state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Successful login for an enabled, non-locked user MUST retain the existing access-token
  response contract and additionally create one renewable-session family and its initial refresh
  credential. Failed login MUST create neither a family nor a refresh credential.
- **FR-002**: Refresh credentials MUST be opaque, cryptographically random values and MUST NOT be
  usable as JWT access credentials. The system MUST persist only a cryptographic representation
  sufficient to verify a received credential; it MUST never persist its complete raw value.
- **FR-003**: Each refresh credential and family MUST support a stable family association, creation
  and expiration instants, revocation state, rotation/replacement relationship, and detection that
  the credential was previously consumed. All persisted authentication instants MUST be UTC.
- **FR-004**: Refresh credentials MUST have an externally configurable absolute expiration with a
  default of seven days. The phase MUST NOT add sliding expiration or an independent session
  lifetime beyond that absolute expiration.
- **FR-005**: Browser refresh credentials MUST be sent only through an HttpOnly cookie, MUST be
  Secure in production, and MUST use `SameSite=Strict` in the defined same-origin deployment.
  The refresh credential MUST not be exposed to JavaScript, and this feature MUST NOT require
  browser persistence of access tokens in localStorage.
- **FR-006**: `POST /api/auth/refresh` MUST not require a currently valid access token. It MUST
  accept a valid refresh credential, verify its authenticity, expiration, revocation and prior-use
  state, and verify that its associated user remains enabled and is not currently locked by
  Identity.
- **FR-007**: A successful refresh MUST invalidate the presented credential, issue a replacement
  credential in the same family, update the browser cookie, and return a newly issued access token
  with expiration information and the user’s current identity and role claims.
- **FR-008**: An invalid, expired, unknown, revoked, malformed, previously unusable, disabled-user,
  or locked-user refresh attempt MUST issue no new credentials and MUST return `401 Unauthorized`
  without exposing token, session, family, user-state, or replay-state details.
- **FR-009**: A refresh credential that was successfully rotated MUST never be accepted again. Its
  reuse MUST revoke its entire family, prevent every later renewal from that family, and record a
  security event without raw credentials or cryptographic secrets.
- **FR-010**: Under simultaneous attempts to refresh with the same credential, the system MUST
  ensure at most one attempt consumes it and establishes a valid replacement continuation. No
  other concurrent attempt may issue an access token or replacement refresh credential.
- **FR-011**: Browser refresh and logout requests MUST be accepted only from the configured external
  frontend origin. Their cookie-dependent behavior MUST include the baseline-required CSRF/origin
  protection, without enabling unnecessary same-origin CORS or deferring these applicable controls.
- **FR-012**: `POST /api/auth/logout` MUST revoke the renewable session or family represented by the
  browser refresh credential, invalidate or clear that cookie, and prevent further renewal through
  it. Logout MUST be idempotent from the client perspective, including when its credential has
  already expired, been revoked, or is absent.
- **FR-013**: Logout, rotation, replay detection, account disablement, and administrative session
  revocation MUST NOT create a centralized access-token blacklist, token introspection, or remote
  validation. Access tokens issued before those actions MAY remain cryptographically valid until
  natural expiration, and Business API A and Business API B MUST continue validating JWTs locally.
- **FR-014**: `POST /api/admin/users/{id}/revoke-sessions` MUST require the established valid
  Administrator authentication and authorization, revoke all active renewable-session families for
  the identified user, and preserve existing administrative `401`, `403`, and `404` conventions.
- **FR-015**: Disabling a user through the existing administrative behavior MUST still prevent new
  login and additionally revoke every active renewable-session family for that user. Re-enabling
  the user MUST NOT reactivate any revoked family, while a later successful login may establish a
  new one. The Phase 3 last-enabled-Administrator protection remains unchanged.
- **FR-016**: Refresh-token state MUST be owned exclusively by Authentication API, survive ordinary
  application restarts and the required persistent-storage lifecycle, and remain unavailable to
  Business API A and Business API B. Pending schema changes MUST remain managed by Authentication
  API startup, with no external migration or bootstrap service.
- **FR-017**: Security-relevant events for refresh-token replay, logout, and session revocation
  MUST be recorded through the existing logging facilities with UTC timestamps and correlation
  where available. Application logs MUST NOT contain raw refresh tokens, access tokens, passwords,
  token hashes, cryptographic keys, or configuration secrets.
- **FR-018**: The phase MUST preserve existing Identity credential verification, lockout and account
  status checks; existing JWT issuance; existing administrative authorization; and the Phase 1–3
  public behavior except for the explicitly stated login extension, session revocation endpoint,
  and disablement extension.

### Applicable Non-Functional Requirements

- **NFR-001**: Verification MUST use a deliberately small, high-value set of consolidated scenarios
  that exercises actual persistent identity and session behavior, including restart persistence,
  rather than substitutes that do not preserve relevant persistence or concurrency behavior.
- **NFR-002**: Verification MUST demonstrate login-session creation, rotation, expired and unknown
  rejection, replay-driven family revocation, one-success-only concurrent consumption, locked and
  disabled rejection, disablement and administrative all-family revocation, idempotent logout,
  browser cookie delivery and invalidation, local consumer JWT validation, and Phase 1–3
  regression behavior.
- **NFR-003**: Time-dependent verification MUST use deterministic control where supported by the
  existing architecture and baseline; it MUST not depend on arbitrary waits.
- **NFR-004**: The feature must not add a separate session database, permanent service, distributed
  session infrastructure, centralized JWT validation, or consumer-API access to session data.

### Key Entities *(include if feature involves data)*

- **Renewable session family**: The common security boundary for all successive refresh credentials
  created from one successful login. It belongs to one user and can be revoked as a whole because
  of replay, logout, disablement, or an administrative action.
- **Refresh credential**: An opaque, random browser credential belonging to one family. It has
  creation and absolute-expiration instants, a secure persisted verifier, revocation/consumption
  state, and a relationship to its replacement after rotation. Its raw value is never persisted.
- **Session revocation**: An irreversible state for a credential or family that prevents further
  access-token renewal but does not invalidate already issued access tokens before their expiry.

### Scope Exclusions

This feature MUST NOT introduce:

- Change-password, forgot-password, reset-password, SMTP, email delivery, or session revocation
  triggered by future password-change or reset workflows.
- JWT access-token blacklists, centralized JWT validation, token introspection, JWKS, or automatic
  signing-key rotation.
- Redis, distributed session infrastructure, a second session database, or a new permanent service.
- Session listing, dashboards, additional session-management endpoints, consumer API business
  functionality, frontend implementation, or full reverse-proxy deployment.
- General application rate-limiting policies, full Phase 7 hardening, or unrelated Phase 7 or
  Phase 8 observability and infrastructure work.
- Speculative future-phase interfaces, abstractions, entities, or services.

## Traceability

| Feature area | Normative sources |
|---|---|
| Refresh credential properties, persistence, rotation, expiration | SRS FR-REFRESH-001 through FR-REFRESH-012; NFR-TIME-001 through NFR-TIME-002; Roadmap §10.2 and §10.5 |
| Browser transport and request protection | SRS FR-REFRESH-013 through FR-REFRESH-016; NFR-CORS-001 through NFR-CORS-003; NFR-CSRF-001 through NFR-CSRF-004 |
| Refresh endpoint and replay response | SRS FR-REFRESH-ENDPOINT-001 through FR-REFRESH-ENDPOINT-005; Roadmap §10.3 through §10.6 |
| Logout and residual access-token validity | SRS FR-LOGOUT-001 through FR-LOGOUT-007; Roadmap §10.7 |
| Administrative and disablement session revocation | SRS FR-USER-008 through FR-USER-014; FR-AUTHZ-001 through FR-AUTHZ-005; Roadmap §10.3 and §10.8 |
| Local consumer JWT validation | SRS FR-JWT-012; NFR-AVAIL-001 through NFR-AVAIL-003; Roadmap §10.1 and §10.10 |
| Persistence ownership and startup | SRS CR-DATA-003 through CR-DATA-005; SRS NFR-DB-INIT-001 through NFR-DB-INIT-014; Constitution Principle VII |
| Logging and secret protection | SRS NFR-LOG-001 through NFR-LOG-005; NFR-SEC-TOK-001 through NFR-SEC-TOK-002; Roadmap §10.6 |
| Verification and gate | SRS TEST-020 through TEST-025, TEST-047; Roadmap §10.9 through §10.10; Constitution Principles I, II, VI, and VII |

**Phase-boundary note**: SRS NFR-SEC-BF-006 through NFR-SEC-BF-009 name refresh among sensitive
endpoints subject to differentiated application rate limiting, but the roadmap assigns general
application rate limiting to Phase 7 and expressly excludes it from Phase 4. This is a timing
conflict in the baseline, not a Phase 4 authorization to implement rate limiting early. Phase 4
implements the applicable cookie origin/CSRF requirements above; rate-limiting delivery requires
an explicit baseline sequencing decision before it is planned.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the verified lifecycle, 100% of successful sign-ins establish a renewable session,
  one valid refresh returns a new access token and replacement browser credential, and logout
  prevents all later renewal through its session while remaining successful when repeated.
- **SC-002**: In every verified rotation case, the presented refresh credential becomes unusable;
  100% of tested reuses revoke the associated family and prevent its valid replacement credential
  from renewing.
- **SC-003**: In every verified simultaneous-use scenario, no more than one request using the same
  refresh credential succeeds and no rejected concurrent request receives new credentials.
- **SC-004**: In every verified disablement and administrative-revocation scenario, all applicable
  renewable-session families are unable to renew; re-enabling an account restores none of them.
- **SC-005**: In all verified browser flows, refresh credentials are inaccessible to browser script,
  use the required restrictive cookie properties, are cleared or invalidated on logout, and refresh
  and logout requests from an unconfigured origin are refused.
- **SC-006**: In the Gate G4 evidence, Business API A and Business API B continue to validate
  previously issued, unexpired access tokens locally without session-state calls; no access-token
  blacklist is required for logout, replay, disablement, or administrative session revocation.
- **SC-007**: Gate G4 has evidence that build, applicable focused tests, and Phase 1–3 regression
  verification pass, together with the required complete login → refresh → logout demonstration,
  before Phase 4 is marked complete.

## Assumptions

- Gate G3 is recorded as complete in the roadmap, including its closing commit, so its prerequisite
  does not block this specification.
- The Phase 1 login response’s access-token and expiry fields remain unchanged; Phase 4 adds only
  the browser refresh-cookie delivery and no raw refresh value to its response body.
- A “corresponding session” for logout means the family identified by the submitted browser refresh
  credential; this aligns with family-wide replay containment while preserving separate concurrent
  login families.
- The existing configured external frontend origin supplies the authoritative browser origin for
  refresh and logout validation. No frontend or reverse-proxy implementation is introduced here.
- The SRS gives the absolute seven-day default and external configurability; it intentionally does
  not authorize a separate sliding or independent family lifetime, so none is assumed.
- HTTP body schemas, cookie name/path/domain attributes, and the precise internal atomicity
  mechanism are design-contract details to be decided in planning, provided they satisfy these
  public behavior and security requirements.
