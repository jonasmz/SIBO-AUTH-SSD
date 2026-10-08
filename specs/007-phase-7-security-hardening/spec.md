# Feature Specification: Phase 7 — Security Hardening

**Feature Branch**: `007-phase-7-security-hardening`  
**Created**: 2026-10-08  
**Status**: Draft  
**Input**: Phase 7 — Authentication API Security Hardening

## Clarifications

### Session 2026-10-08

- Q: Should the service ship documented default values for the request limits, or make them required settings with no default? → A: Documented defaults for the four endpoint policies and the per-address recovery limit, fixed during planning as an explicit, conservative project decision and overridable by external configuration.
- Q: How much of the reverse proxy's first limiting layer does this phase deliver? → A: Besides defining the application-side trust contract, the phase delivers a documented reference proxy configuration (first-layer limits and forwarded headers) and verifies it once in a disposable proxy container used only by acceptance; no service is added to the deployment and the full Phase 8 deployment is not anticipated.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Protection Against Brute Force and Request Abuse (Priority: P1)

An operator wants repeated credential guessing and recovery abuse to be constrained at two
independent levels: per account (Identity lockout) and per network origin (request limits on the
anonymous authentication endpoints), without changing how those endpoints answer legitimate use.

**Why this priority**: The anonymous endpoints are the attack surface of the whole system and are
all in place after Phase 6; lockout and request limits are the primary defence against guessing and
against using the email service to harass one address.

**Independent Test**: Make repeated failed logins for one account and verify it locks and later
recovers; exceed each of the four endpoint limits from one origin and verify `429`; verify that
neither mechanism affects the other.

**Acceptance Scenarios**:

1. **Given** an enabled account, **When** five consecutive login attempts with a wrong password are
   made, **Then** the account is locked and even the correct password is refused with the same
   generic failure as any other credential failure; **When** the lockout period has elapsed,
   **Then** the correct password authenticates again.
2. **Given** no explicit lockout configuration, **When** the service starts, **Then** the initial
   values are five consecutive failures and a fifteen-minute lockout, and an operator can change
   both externally without rebuilding.
3. **Given** a lockout counter above zero, **When** the account signs in successfully, **Then** the
   counter follows Identity's established behavior (reset on success).
4. **Given** the configured limit of any one of login, refresh, forgot-password, and reset-password,
   **When** one origin exceeds it, **Then** further requests from that origin to that endpoint
   receive `429 Too Many Requests` until the limit window allows traffic again, while the other
   three endpoints remain governed only by their own limits.
5. **Given** requests that stay within the limits, **When** they are made, **Then** every endpoint
   answers exactly as before this phase.
6. **Given** one origin that exhausted its login limit, **When** a different origin tries the same
   account, **Then** that attempt is judged only by account lockout; **Given** an account that is
   locked, **When** a new origin attempts to sign in, **Then** it is judged only by the lockout.
   Neither mechanism reads or resets the other's state.
7. **Given** repeated recovery requests naming the same address (in any letter case) from varying
   origins, **When** the per-address allowance is exceeded, **Then** further requests naming that
   address receive `429`, whether or not an account exists for it.

---

### User Story 2 - Trusted Network and Browser Boundaries (Priority: P1)

An operator wants the service to know the real client origin when it runs behind the designated
reverse proxy, to never let a client forge it, and to accept cookie-dependent browser operations
only from the intended frontend.

**Why this priority**: Every per-origin limit and security log is meaningless if the origin can be
spoofed, and the refresh cookie travels automatically with browser requests.

**Independent Test**: Send requests with forged forwarding headers directly and through a trusted
proxy and compare the origin the service acts on; verify refresh-cookie attributes, logout cleanup,
and rejection of cross-origin refresh and logout.

**Acceptance Scenarios**:

1. **Given** a request from a configured, authorized proxy carrying forwarded address and scheme
   headers, **When** it is processed, **Then** the effective client address and scheme are the
   original ones, and per-origin limits and logs use that address.
2. **Given** a request from any other source carrying forwarded headers, **When** it is processed,
   **Then** those headers have no effect: the effective address is the real connecting address, so a
   client can neither choose its rate-limit identity nor reset it by rotating a forged value.
3. **Given** no authorized proxy is configured, **When** forwarded headers arrive, **Then** none are
   honored.
4. **Given** a successful login or refresh, **When** the response is inspected, **Then** the refresh
   cookie is inaccessible to script, restricted to the authentication path, strictly same-site, never
   readable through `document.cookie`, and marked secure in production.
5. **Given** a logout, **When** it completes, **Then** the cookie is cleared with the same
   attributes, and refresh with the old value fails.
6. **Given** a refresh or logout request whose origin is not the configured frontend origin
   (different, missing, or malformed), **When** it arrives, **Then** it is refused before the
   cookie is processed and no cross-origin access is enabled; **Given** the configured origin,
   **Then** the request is processed as before.
7. **Given** the same-origin deployment, **When** the service is inspected, **Then** no
   cross-origin resource sharing is enabled; and no permissive any-origin credentialed policy exists
   in any configuration.

---

### User Story 3 - Confidential Authentication Responses (Priority: P2)

A user and an operator want authentication failures and recovery flows not to reveal which accounts
exist or what state they are in, nor to leak credentials, while operators can still diagnose the
real cause internally.

**Why this priority**: Enumeration and secret leakage undermine every other control; most of this
behavior exists from earlier phases and is verified and completed here.

**Independent Test**: Compare login responses for unknown, wrong-password, locked, and disabled
accounts; compare recovery and reset responses; scan responses and logs for secrets after exercising
every flow.

**Acceptance Scenarios**:

1. **Given** an unknown account, a wrong password, a locked account, and a disabled account,
   **When** each signs in, **Then** status, message, and structure are indistinguishable.
2. **Given** a login for an unknown account, **When** it is processed, **Then** it performs the same
   password-verification work as a wrong-password attempt instead of ending early; no constant time
   or artificial delay is required.
3. **Given** forgot-password and reset-password requests for existing, unknown, and disabled
   accounts, **When** they are made, **Then** their responses reveal nothing about account
   existence or state, including when an internal step fails.
4. **Given** any of those failures, **When** operators read the logs, **Then** each event identifies
   its real internal cause (unknown account, wrong password, locked, disabled, rate limited) with
   UTC time and correlation, without secrets.
5. **Given** every flow exercised, **When** all responses and logs are scanned, **Then** none
   contains a plaintext password, a complete access token, a refresh credential, a reset token, an
   RSA private key, an SMTP credential, or another configuration secret.

### Edge Cases

- A request rejected by a request limit never reaches credential checking, so it neither increments
  nor resets any lockout counter, and the `429` response contains no account information.
- A locked account's lockout is not extended by the rate limiter, and a rate-limited origin is not
  locked out of other origins' accounts.
- Request-limit counters live in the running service only: a restart resets them, and a deployment
  with several instances limits each instance separately (single-instance architecture).
- A forged forwarding header sent through an authorized proxy chain is accepted only as far as the
  authorized hops reach; entries before the first authorized hop are ignored.
- An IPv4/IPv6 representation of the same client counts as one origin.
- Disabling or locking an account never changes the login response shape.
- Change-password keeps the approved behavior: an incorrect current password counts as a failed
  attempt through Identity, a policy-rejected new password does not.
- Administrative and authenticated endpoints are not additionally request-limited unless they are
  anonymous and sensitive (the four listed endpoints).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Identity lockout MUST apply an initial limit of five consecutive failed password
  attempts and an initial lockout of fifteen minutes when no explicit configuration is supplied, and
  both MUST be externally configurable without rebuilding.
- **FR-002**: A failed login MUST be counted by Identity's own failed-attempt mechanism; a
  successful login MUST follow Identity's established counter behavior; a locked account MUST NOT
  authenticate until the lockout expires, after which it MUST authenticate normally. No custom
  counter or independent lockout mechanism MAY be introduced.
- **FR-003**: Login, refresh, forgot-password, and reset-password MUST each have an independent,
  externally configurable request-limit policy applied per effective client origin; exceeding one
  endpoint's policy MUST NOT consume or reset another's.
- **FR-004**: An exceeded policy MUST produce `429 Too Many Requests` using the established
  problem-details error convention, with no account, token, or policy-internal information beyond
  what is needed to retry.
- **FR-005**: Requests within the limits MUST receive exactly the responses defined by Phases 1–6.
- **FR-006**: Account lockout and per-origin request limits MUST remain independent: neither reads,
  sets, or resets the other's state, and the infrastructure-level limit at the proxy MUST NOT replace
  Identity lockout.
- **FR-007**: Forgot-password MUST also be limited per normalized email address, in addition to the
  per-origin limit, using the same normalization as account lookup, counting every submitted address
  whether or not an account exists, and without external storage.
- **FR-008**: When running behind the designated reverse proxy, the service MUST reconstruct the
  original client address and scheme from forwarded headers, and MUST do so only for requests coming
  from explicitly configured proxies or networks; with none configured it MUST honor no forwarded
  header.
- **FR-009**: Forwarded headers from any other source MUST NOT change the effective client address
  or scheme, and the effective address MUST be the one used by request limits and security logs.
- **FR-010**: The service MUST be verifiably safe at the proxy boundary: the trusted-proxy
  configuration, the headers the proxy must set or overwrite, and the behavior for untrusted
  sources MUST be defined and exercised, including a first limiting layer at the proxy, without
  delivering the full production proxy and frontend deployment of Phase 8.
- **FR-011**: The refresh cookie MUST remain inaccessible to script, restricted to the
  authentication path, strictly same-site, secure in production, cleared with matching attributes on
  logout, and unchanged in name, rotation, and replay behavior.
- **FR-012**: Refresh and logout MUST accept browser requests only from the configured frontend
  origin and refuse every other, missing, or malformed origin before processing the cookie;
  alternative cross-origin access MUST NOT be enabled unless a deployment needs it, and then only for
  an explicit origin list, never for any origin with credentials in production. No additional
  anti-CSRF mechanism MUST be added where these controls already satisfy the requirement.
- **FR-013**: Login MUST present an indistinguishable response (status, message, structure) for an
  unknown account, a wrong password, a locked account, and a disabled account.
- **FR-014**: Login for an unknown account MUST perform password-verification work equivalent to a
  wrong-password attempt (SRS NFR-SEC-ENUM-004); neither constant timing nor artificial delays are
  required.
- **FR-015**: Forgot-password and reset-password MUST NOT reveal account existence or state, including
  when an internal step (token generation, delivery, persistence of the response path) fails.
- **FR-016**: Internal security events MUST let operators distinguish the real cause of a login
  failure (unknown account, wrong password, locked, disabled), a lockout being applied, and a request
  limit being applied, each with UTC time and correlation where available, and none MAY contain
  passwords, complete access or refresh tokens, reset tokens, RSA private keys, SMTP credentials, or
  other configuration secrets.
- **FR-017**: Responses and logs of every authentication and recovery flow MUST NOT contain those
  secrets, and the phase MUST preserve all structured security events from earlier phases.
- **FR-018**: The phase MUST NOT introduce new product endpoints, new refresh-session semantics,
  JWT revocation, external or database-backed limit storage, additional permanent services, a new
  logging framework, or work belonging to Phase 8, and MUST preserve Phase 1–6 behavior.
- **FR-019**: The four endpoint policies and the per-address recovery limit MUST each ship with a
  documented default value so that no new setting is required to start the service; those values
  are an explicit project decision recorded during planning (the baseline gives none), chosen
  conservatively, and every one MUST be overridable through external configuration without
  rebuilding.
- **FR-020**: Besides the application-side trust contract, the phase MUST deliver a documented
  reference configuration for the designated reverse proxy that applies a first layer of request
  limiting and sets or overwrites the forwarded headers, and MUST verify it once through a
  disposable proxy used only by the acceptance procedure, showing that the real client origin
  reaches the application and a client-supplied forwarded header does not. The configuration MUST NOT
  add a service to the deployment topology and MUST NOT include the frontend or the complete
  production deployment, which belong to Phase 8.

### Applicable Non-Functional Requirements

- **NFR-001**: Verification MUST use a small set of consolidated scenarios on the existing Identity,
  SQLite, integration-test, and Compose acceptance facilities, covering: five-failure lockout and its
  expiry; each of the four endpoint limits and the per-address recovery limit with correct `429`;
  independence of lockout and origin limits; forged forwarded headers having no effect while a
  trusted proxy's headers do; cookie attributes and logout cleanup; rejected cross-origin refresh and
  logout with same-origin acceptance; login, recovery, and reset equivalence; and secret-free
  responses and logs.
- **NFR-002**: Verification MUST be deterministic, with no waits or sleeps for elapsed time and no
  timing thresholds. Where the framework exposes no time seam (Identity lockout and the
  framework request limiter read the system clock), time is controlled through the state itself:
  lockout expiry by moving the persisted lockout end into the past, and window renewal by
  asserting the window the service reports (`Retry-After` present and no longer than the configured
  window) rather than by waiting for it; unknown-account equivalence is verified by the work
  performed, not by measured duration.
- **NFR-003**: The feature MUST NOT add Redis, distributed counters, external caches, database-backed
  limit storage, a gateway service, or speculative abstractions.

### Key Entities *(include if feature involves data)*

- **Request-limit policy**: A named, externally configured allowance for one endpoint (or one
  normalized address), evaluated per effective client origin and held in the running service only.
- **Trusted proxy set**: The explicitly configured proxies or networks whose forwarded headers are
  honored; empty means none.
- **Effective client origin**: The network address (and scheme) the service acts on after applying
  the trusted proxy set; the single identity used by request limits and security logs.
- **Lockout state**: Identity's own per-account failed-attempt counter and lockout expiry, unchanged
  in ownership.

### Scope Exclusions

This feature MUST NOT introduce new product endpoints, user or role administration features, MFA,
password history or expiry, new refresh-session semantics, distributed JWT revocation, Redis or
external or database-backed limit storage, additional permanent services, the Angular frontend, the
final production deployment, backup and restore, the complete operational logging infrastructure,
final OpenAPI/Scalar integration, or Phase 8 end-to-end deployment acceptance.

## Traceability

| Feature area | Normative sources |
|---|---|
| Identity lockout | SRS NFR-SEC-BF-001 through -005; Roadmap §13.2 |
| Request limits and recovery limit | SRS NFR-SEC-BF-006 through -011, TEST-044, TEST-045; Roadmap §13.3 |
| Proxy layer and forwarded headers | SRS NFR-SEC-BF-012, -013, NFR-NET-001 through -003; Technical Constraints §37 and proxy sections; Roadmap §13.4 |
| Refresh cookie | SRS NFR-CSRF-002, -004, FR-LOGOUT-002, -003; Roadmap §13.5 |
| CSRF, origin, CORS | SRS NFR-CSRF-001, -003, NFR-CORS-001 through -003; Roadmap §13.6 |
| Anti-enumeration | SRS NFR-SEC-ENUM-001 through -005; Roadmap §13.7 |
| Secret protection and security logging | SRS NFR-LOG-001 through -004, NFR-CONFIG-002, -003; Technical Constraints §15.7; Roadmap §13.8 |
| Verification and gate | Roadmap §13.9 and §13.10 (Gate G7); Constitution Principles I, II, V, VI, VII |

**Dependency note**: Gate G6 is recorded as closed in the roadmap (approved 2026-10-08, Phase 6
closing commit merged to `main`), so its prerequisite does not block this specification. The
forgot-password and refresh limiting deferred by the Phase 4 and Phase 6 specifications is delivered
here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the verified lifecycle, five consecutive failed logins lock the account in 100% of
  cases, the correct password is refused while locked and accepted after expiry, and the default
  values are five failures and fifteen minutes unless configured otherwise.
- **SC-002**: Each of the four endpoint policies returns `429` once exceeded from one origin while
  the other three keep answering normally, and a recovery limit per normalized address returns `429`
  for existing and unknown addresses alike.
- **SC-003**: Lockout and request limits are independent in 100% of verified cases: neither mechanism
  changes the other's state.
- **SC-004**: A forged forwarding header from an untrusted source never alters the effective client
  origin in any verified case, while the same header from a configured proxy does.
- **SC-005**: The refresh cookie carries every required attribute on login and refresh, is cleared on
  logout, and refresh and logout from any origin other than the configured one are refused in 100% of
  verified cases while the configured origin is accepted.
- **SC-006**: Login responses for unknown, wrong-password, locked, and disabled accounts are
  identical in 100% of verified cases, and forgot-password and reset-password reveal no account state.
- **SC-007**: A scan of every response and log produced by all verified flows finds no password,
  complete token, refresh credential, reset token, private key, or SMTP credential.
- **SC-008**: Gate G7 evidence shows correct lockout, complete request limiting, proxy trust,
  secure cookies, origin protection, enumeration resistance, secret-free logs, a passing build and
  tests, and passing Phase 1–6 regression before the phase is marked complete.

## Assumptions

- Gate G6 is closed (see Dependency note); Identity, SQLite, session revocation, the existing
  refresh-cookie and origin controls, and the error conventions are reused unchanged.
- Already satisfied and only verified here: cookie flags and logout cleanup (Phase 4), the exact
  configured-origin check on refresh and logout with no cross-origin sharing enabled (Phase 4),
  indistinguishable login failures with equivalent verification work for unknown, locked, and
  disabled accounts (Phase 1/3), anti-enumeration of forgot and reset (Phase 6), and the Phase 5
  change-password failure counting.
- Genuine gaps this phase closes: the fifteen-minute default lockout, request limits and their `429`,
  per-address recovery limiting, trusted forwarded-header handling, the proxy-boundary contract, and
  internal security events for failed login, lockout, and rate limiting.
- A browser request to refresh or logout with no origin keeps being refused, as decided in Phase 4;
  non-browser clients are outside the architecture (the only caller is the same-origin frontend), so
  no further origin policy is assumed.
- The reference proxy configuration is an operator artifact for the first limiting layer; its
  complete production form (frontend routing, TLS, Angular files) is delivered by Phase 8.
- Request limits are held in memory by the single running instance; a restart resets them and no
  external storage is used (SRS NFR-SEC-BF-010).
- The default limit values are a project decision made during planning, not a normative figure, and
  are documented with the operator guidance so they can be reviewed and tuned.
- A `429` carries no account information; including a retry hint is allowed but not required.
- Request and response schemas, header names, and exact configuration key names are contract details
  settled in planning, provided they satisfy the behavior above.
