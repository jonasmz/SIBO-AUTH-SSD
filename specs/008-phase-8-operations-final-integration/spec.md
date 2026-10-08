# Feature Specification: Phase 8 — Operations, Deployment Integration and Final Acceptance

**Feature Branch**: `008-phase-8-operations-final-integration`  
**Created**: 2026-10-08  
**Status**: Draft  
**Input**: Phase 8 — Operations, Deployment Integration and Final Acceptance

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operational Visibility and API Documentation (Priority: P1)

An operator wants the deployed system to leave persistent, correlated, secret-free diagnostic logs and
to publish an accurate, read-only description of the Authentication API, so that the system can be
inspected and operated safely without exposing documentation tooling to the Internet.

**Why this priority**: Without durable logs and an accurate contract the other deployment outcomes
cannot be diagnosed or reviewed, and the baseline requires both before the project can be closed.

**Independent Test**: Run the system, exercise the security-relevant operations, and verify that each
produces a structured, UTC-stamped, correlated entry both on the console and in a persistent daily
file that survives container recreation and contains no secret; then verify the generated contract and
its read-only viewer in a development configuration and their absence from outside in a production one.

**Acceptance Scenarios**:

1. **Given** a running system, **When** login (success and failure), account lockout, rate limiting,
   logout, password change, password reset, user creation, user enable and disable, role assignment
   and removal, refresh-credential reuse detection, and session revocation occur, **Then** each
   produces an identifiable structured event with UTC time and a correlation or trace identifier.
2. **Given** the same events, **When** the console output and the persistent log files are inspected,
   **Then** both carry the same events, files are named by UTC day, older files beyond the configured
   retention (30 days unless changed) are removed, and the files remain after the containers are
   recreated or the Compose project is torn down with its volumes.
3. **Given** heavy request activity, **When** events are written to the persistent file, **Then**
   writing does not block request handling and concurrent writers never interleave or lose lines.
4. **Given** any flow, **When** responses, console output, and files are scanned, **Then** none contains
   a plaintext password, a complete access or refresh token, a password-reset token, an RSA private
   key, an SMTP credential, or another configuration secret.
5. **Given** a development configuration, **When** the contract and the documentation viewer are
   requested, **Then** the contract describes every public endpoint with its method, route, request and
   response structures, status codes, error responses, and Bearer authentication where it applies, and
   the viewer is read-only: no request execution, no API client, no credential persistence.
6. **Given** a production configuration, **When** the contract or the viewer is requested from outside
   the system, **Then** it is not available (or is limited to explicitly authorized networks).

---

### User Story 2 - Unified and Secure Deployment (Priority: P1)

An operator deploys the whole system through Docker Compose with exactly four permanent functional
services and a single external entry point, starting from empty storage, without any extra service or
manual database or administrator step.

**Why this priority**: The system is only an operable product when it starts and is reachable as one
unit exactly as the baseline defines it.

**Independent Test**: Start the production-equivalent configuration from empty external storage and
verify the four services, the single entry point and routing, the unreachable backends, the key
isolation, and the working initial administrator, using only `docker compose up`.

**Acceptance Scenarios**:

1. **Given** empty external storage and the required configuration, **When** only `docker compose up -d`
   is run, **Then** exactly four permanent services (frontend, auth-api, api-a, api-b) run, the schema
   and the initial administrator are created by Authentication API itself, and no migration, bootstrap,
   gateway, database, cache, secrets, backup, or observability service exists.
2. **Given** the running system, **When** a browser uses the single external origin, **Then** its
   authentication requests reach Authentication API, its Business API A requests reach API A, and its
   Business API B requests reach API B, each through the frontend service's reverse proxy only, and the
   application files are served from that same origin.
3. **Given** the production configuration, **When** the host's network interfaces are probed, **Then**
   no backend service's port is reachable directly from outside; only the frontend entry point is.
4. **Given** the deployment, **When** the mounted files of each service are inspected, **Then** the
   Business APIs hold the public key only, Authentication API holds the private key, and the browser
   never receives it.
5. **Given** a request through the entry point, **When** Authentication API evaluates it, **Then**
   forwarded-header trust, request limits, and the origin check behave exactly as verified in Phase 7,
   including the first limiting layer at the proxy.
6. **Given** a login, refresh, and logout performed through the entry point, **When** the browser
   exchanges the refresh cookie, **Then** the cookie is sent back to the refresh and logout routes
   under the browser-visible paths and the full session lifecycle works.

---

### User Story 3 - Persistence and Recovery (Priority: P1)

An operator needs the database, the Data Protection key ring, the signing key, and the logs to outlive
containers and the Compose project, and needs a documented SQLite backup that is proven restorable.

**Why this priority**: Losing the database or the signing key would invalidate every account and
session; recoverability must be demonstrated, not assumed.

**Independent Test**: In a disposable production-equivalent environment establish identifiable state,
restart, rebuild, recreate, and tear down with volumes, restart again and verify the state; then back
up the live database, restore it elsewhere, start the service on it, and authenticate a known account.

**Acceptance Scenarios**:

1. **Given** a running system with users, roles, a changed administrator password, and active refresh
   sessions, **When** it is restarted, rebuilt, or its containers recreated, **Then** users, roles, the
   changed password, the session state, the key ring, and the signing key are intact, and API A and API
   B keep validating tokens signed with the persisted key.
2. **Given** the same state in a disposable acceptance environment, **When** the project is torn down
   with its volumes and started again on the same external storage, **Then** the SQLite database, the
   key ring, and the private key still exist and the same state and token validation hold.
3. **Given** the documentation, **When** an operator looks for them, **Then** every persistent path, its
   ownership and permission requirements, and the explicit action needed to delete persistent data
   are stated, and deleting data requires that explicit action on the external storage.
4. **Given** a database being written to, **When** the documented backup procedure is run, **Then** it
   yields a consistent copy without stopping a permanent service or adding one.
5. **Given** such a backup, **When** it is restored into an isolated disposable environment and
   Authentication API is started on it, **Then** the users, roles, and sessions of the backup are
   present and a known account authenticates; a copy that merely exists is not accepted as evidence.

---

### User Story 4 - Final System Acceptance (Priority: P2)

The project owner wants one reproducible end-to-end scenario, plus the earlier phases' regressions, to
accept the complete MVP on evidence from the real integrated system.

**Why this priority**: Gate G8 declares the project complete only from the integrated result; it
depends on the other stories and adds no behavior of its own.

**Independent Test**: Run the end-to-end procedure from empty external storage against the
production-equivalent stack and the regression of Phases 1–7, and review the recorded evidence.

**Acceptance Scenarios**:

1. **Given** empty external storage, **When** the stack starts, **Then** the schema exists and the
   initial administrator signs in; the administrator replaces the initial password; creates a user and
   assigns a role; that user signs in and both Business APIs accept the token.
2. **Given** that user's session, **When** the refresh credential is used, **Then** it rotates; after
   logout the old credential can no longer renew; the user signs in again.
3. **Given** the user requests password recovery and receives the email, **When** the reset is
   completed, **Then** the previous renewable sessions are revoked and the new password works.
4. **Given** the administrator disables the user, **Then** a new login is refused; **Given** repeated
   failed logins, **Then** the account locks and later recovers; **Given** repeated requests, **Then**
   the application and the proxy limits respond.
5. **Given** the completed run, **When** the system is restarted, recreated, torn down with volumes, and
   a backup is restored into a disposable environment, **Then** the earlier assertions about persisted
   state hold, access from outside succeeds only through the entry point, and the Business APIs cannot
   read the private key.
6. **Given** the regression procedures of Phases 1–7, **When** they run unchanged on the final
   deployment, **Then** they pass and all earlier gates remain passing.

### Edge Cases

- A backup is taken while users sign in or sessions rotate: it must still be a consistent database.
- A backup from an older version is restored: startup applies pending migrations automatically and
  never recreates a database that is already current.
- The persistent log directory is missing or not writable: the deployment problem is reported at
  startup naming only the setting, like the other required storage paths.
- The configured retention is shortened: older files beyond it are removed at the next rotation.
- Two requests log at the same moment: lines are never interleaved or lost.
- A second `docker compose up` on existing storage does not recreate the database, the administrator
  password, or the keys.
- Teardown of the acceptance environment never touches real production storage: acceptance uses only
  disposable paths created for the run.
- A defect found in an earlier phase's behavior is reported against its originating requirement and
  corrected in that phase, not absorbed here.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST record structured events, each with a UTC timestamp and a correlation or
  trace identifier, for: login success and failure, account lockout, rate limiting, logout, password
  change, password reset, user creation, user enable and disable, role assignment and removal,
  refresh-credential reuse detection, and session revocation. Existing events MUST be reused and only
  the missing ones added.
- **FR-002**: Events MUST be emitted on the console in a form suited to container logs and, from the same
  events, to persistent files in a configurable directory, one line per event, in UTF-8, with level,
  category, event identifier when present, timestamp, and correlation when present.
- **FR-003**: File writing MUST be thread-safe and MUST NOT perform significant blocking input or output
  inside request handling.
- **FR-004**: Log files MUST rotate daily by UTC date, retain 30 days by default with the retention
  configurable, and MUST be stored on storage independent of the Compose project's lifecycle so that
  recreation and teardown with volumes do not delete them.
- **FR-005**: Console output, files, and responses MUST NOT contain plaintext passwords, complete access
  or refresh tokens, password-reset tokens, private keys, SMTP credentials, or other configuration
  secrets; exceptions MUST be recorded without exposing them.
- **FR-006**: Authentication API MUST publish an OpenAPI contract through the official mechanism of the
  platform describing every existing public endpoint, method, route, request and response structure,
  status code, error response, and Bearer authentication requirement, without changing any endpoint's
  behavior.
- **FR-007**: A read-only documentation viewer MUST present that contract with request execution, the API
  client, and credential persistence disabled.
- **FR-008**: In development the contract and viewer MUST be available; in production they MUST NOT be
  reachable from outside, or MUST be limited to explicitly authorized networks.
- **FR-009**: The reference deployment MUST consist of exactly four permanent functional services —
  frontend, auth-api, api-a, api-b — with no migration, bootstrap, gateway, database, cache, secrets,
  backup, or observability service.
- **FR-010**: The frontend service MUST serve the application's static files and act as the reverse proxy,
  so that the browser uses a single external origin and reaches Authentication API, API A, and API B
  only through it, under the browser-visible path prefixes `/auth`, `/api-a`, and `/api-b` (exact paths
  are configurable and not a functional contract).
- **FR-011**: The browser flows that depend on the refresh cookie (login, refresh, logout) MUST work
  end to end through the entry point, including the cookie being returned on the browser-visible
  routes.
- **FR-012**: In the production configuration no backend service MUST publish a port to the host; the
  backends MUST share an internal network with the frontend service, and only the entry point is
  externally reachable.
- **FR-013**: Business APIs MUST receive only the public signing key; the private key MUST be available
  only to Authentication API.
- **FR-014**: The deployment MUST preserve the Phase 7 behavior of trusted-proxy forwarded headers,
  application request limits, the proxy's first limiting layer, and origin checks.
- **FR-015**: The system MUST start from empty storage with `docker compose up` alone: Authentication API
  creates or updates its schema and ensures the initial administrator by itself, idempotently, and is
  not ready if that fails; no external migration or bootstrap command is part of deployment.
- **FR-016**: The SQLite database, the Data Protection key ring, the private signing key, and the log
  files MUST reside on persistent storage independent of the Compose project lifecycle, not solely on
  Compose-managed named volumes, and `docker compose down -v` MUST NOT remove them.
- **FR-017**: After restart, rebuild, or container recreation, users, roles, the administrator's changed
  password, session state, the key ring, and the signing key MUST remain, and API A and API B MUST keep
  validating tokens signed with the persisted key.
- **FR-018**: In a disposable production-equivalent environment, after `docker compose down -v` the
  database, key ring, and private key MUST still exist, and after starting again the same users, roles,
  credentials, and token validation MUST hold. Acceptance MUST never touch real production data.
- **FR-019**: The operations documentation MUST state every persistent path with its ownership and
  permission requirements and the explicit action required to delete persistent data.
- **FR-020**: A documented procedure MUST produce a consistent backup of the SQLite database while it may
  be written to, without a permanent additional service, scheduler, or container, and a documented
  procedure MUST restore it.
- **FR-021**: A real backup MUST be restored into an isolated disposable environment, Authentication API
  MUST be started on it, and users, roles, and the authentication of at least one known account MUST be
  verified; merely copying a file is not evidence.
- **FR-022**: A reproducible end-to-end acceptance procedure MUST run the scenario of User Story 4 from
  empty storage on the integrated system, reusing the Phase 1–7 acceptance procedures where they
  already give the evidence, and MUST demonstrate real integration, not only mocked dependencies.
- **FR-023**: The phase MUST introduce no new endpoint, user-management feature, authentication method,
  recovery mechanism, refresh-token semantics, domain entity, functional table, permanent service, or
  frontend product feature, and MUST NOT redefine or weaken any baseline requirement; a defect found in
  an earlier phase MUST be traced to its originating requirement and corrected in that phase.
- **FR-024**: [NEEDS CLARIFICATION: where does the Angular application served by the frontend service
  come from? The repository contains no Angular source or compiled output. Does the project owner
  supply the compiled static files as an input that the frontend service serves, with Phase 8
  delivering the service, its proxy configuration, and acceptance with a minimal placeholder page; or
  must Phase 8 itself produce a minimal application?]

### Applicable Non-Functional Requirements

- **NFR-001**: Verification MUST prioritize observable integration and operational acceptance over new
  unit tests and MUST add only scenarios that close a demonstrated gap: persistent secret-free logs;
  contract completeness and restricted exposure; routing and backend isolation; deployment without
  manual steps; ordinary persistence and `down -v` survival; backup and functional restoration; the
  end-to-end scenario; and regression of Phases 1–7.
- **NFR-002**: Acceptance procedures MUST be deterministic and reproducible on a disposable environment,
  MUST not depend on arbitrary waits where the system can report readiness, and MUST clean up
  everything they create.
- **NFR-003**: The phase MUST NOT add an external logging platform, log collector, database,
  observability service, external secret manager, distributed storage, cache, queue, gateway, or
  orchestration beyond the baseline.

### Key Entities *(include if feature involves data)*

- **Persistent assets**: The database, key ring, private and public signing keys, and log files that
  must outlive any container or Compose project.
- **Operational log**: A daily, UTC-named file (and the console stream) of structured events.
- **Backup**: A consistent copy of the database from which a working instance can be rebuilt.
- **Acceptance environment**: A disposable, production-equivalent run on paths created for the run.

### Scope Exclusions

This feature MUST NOT introduce new business endpoints, user-management features, authentication
methods, MFA, additional recovery mechanisms, changes to approved refresh-token semantics, domain
entities, functional database tables, additional permanent services, a new gateway, Kubernetes or
orchestration beyond the baseline, distributed storage, caches, queues, external secret management,
a general-purpose monitoring platform, unrequested frontend functionality, or product features whose
only justification is a final acceptance test.

## Traceability

| Feature area | Normative sources |
|---|---|
| Logging, events, correlation, file logging | SRS NFR-LOG-001 through -005; Technical Constraints §15; Roadmap §14.2 |
| OpenAPI and read-only documentation | SRS NFR-DOC-001 through -004; Technical Constraints §12, §13; Roadmap §14.3 |
| Four-service Compose, single origin, isolation | SRS §4.2, §4.3, NFR-DEPLOY-001 through -010, -014; Technical Constraints §14; Roadmap §14.4 |
| Startup, migrations, bootstrap | SRS NFR-DB-INIT-001 through -014, NFR-HEALTH-001 through -005; Roadmap §14.4 |
| Persistence outside Compose lifecycle | SRS NFR-DEPLOY-005, -006, -011 through -013; Technical Constraints §30; Roadmap §14.5, §14.6 |
| Backup and restoration | SRS NFR-BACKUP-001 through -006; Roadmap §14.7 |
| End-to-end acceptance | SRS acceptance list (item 35 and the MVP acceptance scenarios); Roadmap §14.8 |
| Verification and gate | Roadmap §14.9 (Gate G8); Constitution Principles I, II, VI, VII |

**Dependency note**: Gate G7 is recorded as closed in the roadmap (approved 2026-10-08, closing commit
merged to `main`), so its prerequisite does not block this specification.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every event listed in FR-001 appears with UTC time and a correlation identifier on the
  console and in the daily persistent file in 100% of the verified occurrences, the files survive
  container recreation and `down -v`, and no secret appears in any scanned output.
- **SC-002**: The contract describes all existing public endpoints with methods, routes, request and
  response structures, status codes, errors, and Bearer requirements; the viewer is read-only; both are
  available in development and not reachable from outside in production.
- **SC-003**: From empty external storage, `docker compose up -d` alone yields exactly four permanent
  services, a working initial administrator, and no migration or bootstrap step.
- **SC-004**: Routing through the single entry point works for the application files, Authentication API,
  API A, and API B, no backend port is reachable from outside in the production configuration, and the
  refresh-cookie lifecycle works through that origin.
- **SC-005**: After restart, rebuild, recreation, and `docker compose down -v` plus a new start, 100% of
  the persisted users, roles, credentials, session state, keys, and token validation are intact.
- **SC-006**: A backup taken during writes restores into a disposable environment, the service starts on
  it, and a known account authenticates with its roles in 100% of the verified restorations.
- **SC-007**: The end-to-end scenario passes from empty storage, and the regression of Phases 1–7 passes
  on the final deployment with all earlier gates still passing.
- **SC-008**: Gate G8 evidence shows the complete deployment, no manual migration or bootstrap, critical
  storage independent of Compose, `down -v` survival, backup and restore, routing, end-to-end acceptance,
  a passing build and full test suite, reviewed logs and contract, and updated operations
  documentation, before the project is marked complete.

## Assumptions

- Gate G7 is closed (see Dependency note); all Phase 1–7 behavior, health endpoints, automatic
  migrations and bootstrap, the SMTP email boundary, Phase 7 limits and proxy trust, the reference proxy
  configuration, and the acceptance scripts are reused, not rebuilt.
- Inspected state at specification time — already present: login-failure, lockout, rate-limit,
  refresh-reuse, logout and session-revocation, password-change, and password-reset events, the
  readiness check, automatic startup initialization, bind-mounted database, key ring, and signing key.
  Genuine gaps: no persistent file logging, no OpenAPI contract or viewer, no frontend service or
  production-only (non-published backend ports) configuration, no operational backup and restore
  procedure or evidence, no end-to-end procedure, and no recorded events for login success, user
  creation, user enable and disable, and role assignment and removal.
- Browser-visible path prefixes are configurable (SRS §4.2); how a prefix maps to the service's own
  routes, and the cookie path that results, are design contracts settled in planning, provided the full
  login, refresh, and logout lifecycle works through the single origin.
- In production the contract and viewer are simply not served by Authentication API and not routed by
  the proxy; an authorized-network exception is permitted but not required.
- Health endpoints are for the platform and the proxy's own checks and are not routed to the Internet.
- A missing or unwritable log directory fails startup naming only the setting, consistent with the other
  required storage paths.
- Correlation uses the existing trace identifier carried by every structured event, which SRS
  NFR-LOG-005 accepts as an equivalent mechanism.
- Acceptance uses only disposable directories and a disposable mail sink and never real production
  storage.
