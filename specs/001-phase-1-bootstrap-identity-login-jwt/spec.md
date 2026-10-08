# Feature Specification: Phase 1 — Bootstrap, Identity, Admin, Login and JWT

**Feature Branch**: `001-phase-1-bootstrap-identity-login-jwt`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Phase 1 — Bootstrap + Identity + Admin + Login + JWT

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start a Ready Authentication Service (Priority: P1)

An operator starts Authentication API with its required external configuration and persistent
storage. The service prepares an empty database, creates its initial administrative identity,
and reports that it is ready without any manual database or bootstrap step.

**Why this priority**: The product cannot authenticate anyone until it can start reliably with
its identity data prepared.

**Independent Test**: Start from empty persistent storage, start the service through Docker
Compose, then confirm the service is live and ready and that the initial administrator can be
found through the supported authentication flow.

**Acceptance Scenarios**:

1. **Given** empty configured persistent storage and valid external configuration, **When** an
   operator starts the service through Docker Compose, **Then** the database is prepared,
   `Administrator` and the initial administrator are present, and readiness reports healthy.
2. **Given** a database initialization failure, **When** startup is attempted, **Then** startup
   fails before normal traffic is accepted and the service never announces readiness.
3. **Given** the service process is running, **When** a liveness check is requested, **Then**
   it reports process health without disclosing sensitive details.

---

### User Story 2 - Administrator Signs In and Receives an Access Token (Priority: P2)

The initial administrator signs in with email and password and receives a short-lived access
token that identifies the administrator and can later be verified by authorized consumers.

**Why this priority**: This is the first usable authentication capability and establishes the
access-token contract for later phases.

**Independent Test**: With a ready service, sign in using the initial administrator email and
password; inspect the returned token and expiration information. Attempt invalid credentials
and confirm a generic rejection while failed attempts are recorded by account security controls.

**Acceptance Scenarios**:

1. **Given** the initial administrator exists, **When** valid email and password are submitted,
   **Then** the service returns an access token and its expiration information.
2. **Given** an unknown email or an incorrect password, **When** login is attempted, **Then**
   the service returns the same generic `401 Unauthorized` outcome without revealing which
   credential was invalid.
3. **Given** a failed login attempt, **When** account security state is examined through the
   supported identity behavior, **Then** the failed attempt has been accounted for.

---

### User Story 3 - Preserve Bootstrap State Across Lifecycle Changes (Priority: P3)

An operator restarts or recreates the service and retains the existing identity database and
RSA signing material, so that bootstrap does not duplicate or overwrite established state.

**Why this priority**: A usable identity service must not lose accounts or signing continuity
during ordinary container lifecycle operations.

**Independent Test**: Start the service, change an allowed existing administrator attribute or
otherwise establish observable existing state, restart it, and confirm the role and account are
not duplicated or overwritten. Demonstrate that the configured SQLite file and private signing
material survive `docker compose down -v` because they reside outside Compose-managed volumes.

**Acceptance Scenarios**:

1. **Given** an initialized database with existing administrator changes, **When** the service
   restarts, **Then** it preserves those changes and does not duplicate the administrator or role.
2. **Given** configured persistent storage, **When** Compose-managed volumes are removed and
   the service is started again, **Then** the SQLite data and RSA private signing material remain
   available from their external storage locations.

### Edge Cases

- Invalid or missing required external configuration prevents normal ready-state operation and
  does not expose secrets or internal implementation details.
- A pre-existing database is migrated only as needed; it is neither recreated nor reset during
  startup.
- A missing or inaccessible RSA private key prevents successful token issuance and must not be
  exposed in responses or logs.
- Existing credentials with an incorrect password, unknown credentials, and applicable built-in
  Identity lockout states receive the same externally generic login outcome; the feature does not
  expose account existence. A distinct disabled-user state and its enforcement begin with the
  Phase 3 administrative capability that can create that state.
- The initial administrator's weak first-access password is identified for replacement after
  first access, but password change is not provided by this phase.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST start through Docker Compose using required external
  configuration and persistent storage, without a manual database initialization step, external
  bootstrap script, migration container, or migration command executed by Compose.
- **FR-002**: During startup, the service MUST prepare an empty database and apply pending
  migrations before accepting normal ready-state traffic. This process MUST be idempotent and
  MUST preserve an existing database and its identity data.
- **FR-003**: During initial database preparation, the service MUST ensure exactly one
  `Administrator` role and one initial administrator with internal username `admin`, email
  `admin@local.invalid`, initial password `admin`, and the `Administrator` assignment.
- **FR-004**: Subsequent startups MUST NOT duplicate the initial role or administrator, or
  overwrite the administrator's password, email, role assignments, or other existing changes.
- **FR-005**: The feature's operational guidance MUST identify that the initial `admin` password
  requires replacement after first access; it MUST NOT provide password-change behavior in this
  phase.
- **FR-006**: The service MUST provide anonymous `POST /api/auth/login` accepting an email and
  password. It MUST authenticate by email, apply the identity system's email normalization, and
  use the identity system's credential and failed-attempt accounting behavior.
- **FR-007**: Invalid credentials, including an unknown email or incorrect password, MUST return
  a generic `401 Unauthorized` result that does not disclose whether an account exists.
- **FR-008**: A successful login MUST return an access token and expiration information. It MUST
  NOT create or return a refresh token, refresh cookie, renewable session, or session family.
- **FR-009**: Each issued access token MUST be signed with RS256 using signing material available
  only to Authentication API, and include a stable user identifier, email, applicable role claims,
  issuer, audience, issued time, expiry time, and unique token identifier.
- **FR-010**: The access-token lifetime MUST be supplied through external configuration and
  default to 15 minutes. Token timestamps and expiry evaluation MUST use UTC.
- **FR-011**: The private signing key MUST be supplied outside source control and the container
  image, be accessible only to Authentication API, use restricted access, and persist outside the
  Compose project lifecycle. The feature MUST not disclose it in responses or logs.
- **FR-012**: The service MUST provide `GET /health/live` and `GET /health/ready`. Liveness MUST
  report whether the process is running; readiness MUST reflect database availability and whether
  initialization completed successfully, without exposing sensitive details.
- **FR-013**: SQLite data MUST be owned exclusively by Authentication API, stored outside the
  container's ephemeral layer and Compose-managed project volumes, and have configurable,
  documented persistent storage. The reference deployment MUST use a host bind mount; an
  explicitly externally managed volume is an allowed alternative.

### Applicable Non-Functional Requirements

- **NFR-001**: The service MUST preserve the initial database and RSA private signing material
  across restart, rebuild, and `docker compose down -v` through storage whose lifecycle is
  independent of the Compose project.
- **NFR-002**: Database initialization failure MUST prevent readiness from reporting success and
  MUST provide diagnostic information without exposing secrets.
- **NFR-003**: Authentication failures and health responses MUST not expose passwords, access
  tokens, private keys, configuration secrets, stack traces, or internal storage/identity details.
- **NFR-004**: Phase 1 verification MUST use a small, high-value set of automated tests and
  deployment demonstrations that covers current critical behavior and Gate G1. It MUST NOT rely
  on future-phase capabilities, placeholder tests, or arbitrary coverage targets.

### Key Entities *(include if feature involves data)*

- **Initial administrator**: The known first-access identity with a stable identity, internal
  username, email, password state, and administrative role assignment.
- **Administrative role**: The single canonical role assigned to the initial administrator during
  bootstrap; administrative management of roles is outside this feature.
- **Access token**: A time-limited credential carrying the identity and role information required
  by the SRS; it is not a renewable session.
- **Persistent identity store**: The Authentication API-owned state that contains identities,
  roles, and migration history and survives the service lifecycle.
- **RSA signing material**: The externally supplied private material used only by Authentication
  API to sign access tokens and retained outside the Compose project lifecycle.

### Scope Exclusions

This feature MUST NOT introduce:

- JWT validation integration in Business API A or Business API B.
- Administrative user or role management.
- Refresh tokens, renewable sessions, token rotation, replay detection, logout, or session
  revocation.
- Password change, password recovery, reset tokens, email delivery, SMTP, or email ports.
- Full application rate limiting, reverse-proxy hardening, refresh cookies, or CSRF controls.
- JWKS, automatic signing-key rotation, distributed JWT blacklists, or external identity
  infrastructure.
- Data Protection persistence, final four-service deployment integration, backup/restore, or
  final operational acceptance work assigned to later phases.
- Consumer-API JWT validation and its explicit clock-tolerance policy, assigned to Phase 2.
- Runtime-generated OpenAPI/Scalar publication and final contract review, assigned to Phase 8;
  the Phase 1 design contract remains the source for its three current endpoint contracts.
- Production-release image pinning and digest recording; Phase 1 uses the Technical Constraints'
  permitted development image tags for its development and acceptance Compose artifact.
- Speculative abstractions, persistence structures, contracts, mocks, or placeholders intended
  solely for later phases.

## Traceability

| Feature area | Normative sources |
|---|---|
| Bootstrap identity and role | SRS FR-ADMIN-BOOT-001 through FR-ADMIN-BOOT-008; FR-ROLE-001 through FR-ROLE-003; Roadmap §7.2 |
| First-access credential warning | SRS NFR-SEC-ADMIN-001 and NFR-SEC-ADMIN-003; Roadmap §7.2 |
| Login behavior | SRS FR-ID-001 through FR-ID-005; FR-LOGIN-001 through FR-LOGIN-015; Roadmap §7.2 and §7.4 |
| Access token and signing key | SRS FR-JWT-001 through FR-JWT-009; FR-KEY-001 through FR-KEY-004 and FR-KEY-011 through FR-KEY-013; Roadmap §7.2 and §7.4 |
| Startup, readiness, and persistence | SRS CR-DATA-001 through CR-DATA-009; NFR-DB-INIT-001 through NFR-DB-INIT-014; NFR-HEALTH-001 through NFR-HEALTH-005; NFR-DEPLOY-003 through NFR-DEPLOY-014; Roadmap §7.2, §7.4, and G1 |
| Phase boundary and verification | Constitution Principles I, II, VI, and VII; Roadmap RD-001 through RD-008 and §7.3 through §7.5 |

The final-product portions of SRS FR-LOGIN-009 and FR-LOGIN-011 concerning refresh sessions
and cookies are intentionally deferred to Roadmap Phase 4. This Phase 1 specification covers
only the access-token and expiry-information portion of login; no higher-level conflict exists
because the roadmap defines implementation sequencing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From empty configured persistent storage, one Docker Compose startup produces a
  live and ready Authentication API with the initial administrator and administrative role present,
  without operator-run migration or bootstrap actions.
- **SC-002**: The initial administrator can authenticate by email and password, receive one valid
  RS256-signed access token with all required identity, role, issuer, audience, time, and
  identifier claims, and receive expiration information with a 15-minute default lifetime.
- **SC-003**: Unknown-email and wrong-password login attempts both result in `401 Unauthorized`
  with indistinguishable externally visible credential-failure information, while failed attempts
  are accounted for by the identity system.
- **SC-004**: A restart preserves existing identity state without duplicating or overwriting the
  initial administrator or role; the externally stored SQLite data and RSA private signing
  material also remain present after `docker compose down -v`.
- **SC-005**: Liveness reports a healthy running process, while readiness reports healthy only
  after successful database initialization. An initialization failure terminates startup before
  the service can announce readiness; after successful startup, loss of database availability
  makes readiness report failure.
- **SC-006**: Gate G1 evidence shows build, focused automated verification, startup, feature
  workflow, and regression checks passing before Phase 1 is marked complete.

## Assumptions

- The operator provides valid external configuration, SQLite persistent storage, and RSA signing
  material before starting the service; provisioning those host resources is not a manual
  application bootstrap step.
- The reference deployment may contain only the Phase 1 infrastructure necessary for
  Authentication API; the final frontend, API A, and API B integration remains future work.
- The initial administrator's password-change capability is delivered in Roadmap Phase 5. This
  phase communicates the replacement requirement without implementing that later capability.
- Applicable built-in Identity lockout outcomes honor the SRS generic response contract. The
  disabled-user state and enforcement are introduced with Phase 3 administration, while full
  security-hardening configuration remains assigned to Phase 7.
