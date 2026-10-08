# Feature Specification: Phase 6 — Password Recovery, Reset and Email

**Feature Branch**: `006-phase-6-password-recovery-email`  
**Created**: 2026-10-08  
**Status**: Draft  
**Input**: Phase 6 — Password Recovery, Reset and Email

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Password Recovery Request (Priority: P1)

A user who cannot sign in with their current password asks for recovery instructions using only
their email address, without any authenticated session. The system answers the same way whether or
not the account exists, and an existing, enabled account is sent reset instructions by email.

**Why this priority**: Without a recovery request there is no way to regain access; it is the entry
point of the whole capability and the first place account existence could leak.

**Independent Test**: Request recovery for an existing enabled account and for an unknown address;
verify identical responses, that a message was requested only for the existing account, and that no
response or log contains the reset token.

**Acceptance Scenarios**:

1. **Given** an existing, enabled account, **When** an anonymous caller requests recovery with its
   email, **Then** a temporary reset token is generated and its delivery is requested through the
   configured email provider, and the response never contains the token.
2. **Given** an address that has no account, **When** recovery is requested, **Then** no token is
   generated, no email is requested, and the external response is equivalent to the one in
   scenario 1.
3. **Given** a disabled account, **When** recovery is requested, **Then** no token is generated, no
   email is requested, and the external response is equivalent to the one in scenario 1.
4. **Given** the same address written with different letter case, **When** recovery is requested,
   **Then** it is handled exactly as the existing sign-in and user-administration flows treat email
   case.
5. **Given** a missing, blank, or malformed request, **When** recovery is requested, **Then** it is
   rejected with the established invalid-request convention, independent of whether any account
   exists.

---

### User Story 2 - Password Reset With a Token (Priority: P1)

A user holding a valid reset token submits their email, the token, and a new password, and regains
access with the new password. Invalid, altered, or expired tokens never change anything.

**Why this priority**: This completes recovery; it is the capability that restores access.

**Independent Test**: Obtain a reset token for an account, reset the password with it, then verify
the old password fails, the new one signs in, and each invalid-token case changes nothing.

**Acceptance Scenarios**:

1. **Given** a valid reset token for an enabled account and a new password that satisfies the
   existing Identity policy, **When** reset is requested without any access token or session,
   **Then** the password is replaced, the previous password no longer signs in, and the new one
   does.
2. **Given** a token that is invalid, altered in any way, issued for a different account, or past
   its temporary validity, **When** reset is requested, **Then** it is rejected and the password is
   unchanged.
3. **Given** an unknown email, **When** reset is requested, **Then** the rejection is externally
   equivalent to an invalid token, so account existence is not exposed.
4. **Given** a token that was valid, **When** the account's security state has since changed (for
   example, the same token already used for a successful reset, or the password changed by another
   route), **Then** the token is no longer accepted.
5. **Given** a valid token and a new password that violates the existing Identity policy, **When**
   reset is requested, **Then** it is rejected with the established validation convention and the
   password is unchanged.
6. **Given** a successful reset, **When** the account is inspected, **Then** its email, roles,
   enabled state, and other attributes unrelated to the password are unchanged.

---

### User Story 3 - All Renewable Sessions End After a Reset (Priority: P1)

After a successful reset, every renewable session of the user ends, so no credential obtained
before the reset can keep producing access tokens.

**Why this priority**: A reset usually follows loss of control of the account; leaving older
sessions renewable would defeat it. This is a mandatory Gate G6 outcome and differs from the
authenticated password change, which keeps the caller's own session.

**Independent Test**: Establish two renewable sessions, reset the password, and verify neither can
refresh.

**Acceptance Scenarios**:

1. **Given** a user with several active renewable-session families, **When** a reset succeeds,
   **Then** every one of them is revoked, with no session kept, and none can subsequently refresh.
2. **Given** a failed reset (invalid token, policy violation), **When** the user's sessions are
   inspected, **Then** none is revoked.
3. **Given** an access token issued before the reset, **When** it is presented to the Business APIs
   before its natural expiry, **Then** it remains valid under the unchanged local JWT validation; no
   blacklist or remote check is introduced.
4. **Given** a request for recovery only (no reset), **When** it completes, **Then** no session is
   revoked.

---

### User Story 4 - Recovery Survives Restarts and Redeployments (Priority: P2)

An operator can restart, rebuild, or recreate Authentication API without invalidating reset tokens
that are still within their validity, because the key material that protects them lives in storage
the operator controls, independent of the Compose project.

**Why this priority**: Without it, an ordinary deployment operation silently breaks legitimate
recovery in progress; it is a mandatory Gate G6 outcome but exercised by the P1 flows.

**Independent Test**: Obtain a reset token, restart (and recreate) Authentication API, remove the
Compose project's resources, and verify the token still resets the password.

**Acceptance Scenarios**:

1. **Given** a still-valid reset token, **When** Authentication API restarts or its container is
   recreated, **Then** the token still resets the password.
2. **Given** the Compose project is torn down together with its volumes, **When** the stack is
   started again on the same external key storage, **Then** the same still-valid token is accepted.
3. **Given** the key storage location is set through external configuration, **When** an operator
   follows the documented procedure, **Then** the keys are stored there and only Authentication API
   and the authorized operator can write them; no additional permanent service or external
   key-management service is involved.

### Edge Cases

- Requesting recovery repeatedly for one account simply generates and sends further messages; no
  throttling is added in this phase (see Scope Exclusions).
- A reset token is single-purpose: it cannot authenticate, cannot be used as an access or refresh
  credential, and cannot be used on a different account.
- An email delivery failure never exposes the reset token or SMTP secrets in a response or a log,
  and is recorded with safe diagnostic information.
- Two simultaneous resets with the same token produce at most one success.
- A persistence failure during reset yields the established service-unavailable convention and
  leaves the password and sessions unchanged.
- Passwords, password hashes, security stamps, reset tokens, and SMTP credentials never appear in
  responses or logs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Authentication API MUST provide `POST /api/auth/forgot-password`, which is anonymous
  and accepts an email address; a missing, blank, or malformed request MUST be rejected with the
  established invalid-request convention independent of account existence.
- **FR-002**: The response to a well-formed recovery request MUST be externally equivalent for
  existing, nonexistent, and disabled accounts, in status, body, and headers, and MUST NOT contain
  the reset token.
- **FR-003**: For an existing, enabled account only, the system MUST generate a temporary reset
  token using the mechanisms of ASP.NET Core Identity and request its delivery through the
  configured email provider; for any other account it MUST generate no token and request no email.
- **FR-004**: Email address handling MUST be consistent with the case and normalization behavior
  already used by sign-in and user administration.
- **FR-005**: Authentication API MUST provide `POST /api/auth/reset-password`, which is anonymous,
  requires no access token or session, and requires the email, the reset token, and the new
  password.
- **FR-006**: The reset token MUST be validated through Identity; a token that is invalid, altered,
  issued for another account, expired, or no longer valid because the account's associated security
  state changed MUST be rejected without changing anything.
- **FR-007**: The new password MUST satisfy the existing, externally configured Identity password
  policy and MUST be stored through Identity's supported mechanism; a policy violation MUST be
  rejected with the established validation convention without changing anything.
- **FR-008**: An unknown email, a disabled account, and an invalid token MUST produce externally
  equivalent rejections so account existence and state are not exposed.
- **FR-009**: A successful reset MUST make the new password authenticate and the previous password
  stop authenticating, and MUST NOT change the account's email, roles, enabled state, or other
  attributes unrelated to the password.
- **FR-010**: A successful reset MUST revoke every active renewable-session family of the user
  using the Phase 4 revocation rules, with no session kept, and the change and the revocation MUST
  be observed as one outcome; a failed reset MUST revoke nothing.
- **FR-011**: No access-token blacklist, remote validation, or change to how Business API A and
  Business API B validate JWTs MUST be introduced; previously issued access tokens MAY remain valid
  until their natural expiry.
- **FR-012**: Email sending MUST be requested through an application-level port, and the concrete
  SMTP delivery MUST live outside Domain and Application and outside the business logic.
- **FR-013**: SMTP delivery MUST be configurable externally: host, port, transport security,
  authentication credentials, sender address, and sender display name; credentials MUST come from
  external configuration and never from the image.
- **FR-014**: An email delivery failure MUST be recorded with safe diagnostic information and MUST
  NOT record the reset token or SMTP secrets. [NEEDS CLARIFICATION: what must the caller of
  forgot-password observe when delivery fails for an existing account? Reporting the failure
  would reveal that the account exists, while hiding it keeps the response equivalent but gives the
  user no signal. The baseline requires only that the failure be logged without the token.]
- **FR-015**: Persistent Data Protection key material MUST be stored outside the container's
  ephemeral layer, in a location configurable externally and independent of the Compose project
  lifecycle, so that a restart, rebuild, container recreation, or `docker compose down -v` does not
  invalidate a still-valid reset token.
- **FR-016**: The key storage MUST be writable only by Authentication API and the authorized
  operator, MUST require no external key-management service and no additional permanent service, and
  MUST be documented for the operator, with a host bind mount as the reference deployment.
- **FR-017**: Security-relevant events (recovery requested for a deliverable account, successful
  reset with sessions revoked, delivery failure) MUST be recorded through the existing logging
  facilities with UTC time and correlation where available; logs MUST NOT contain passwords,
  hashes, security stamps, reset tokens, access or refresh credentials, or SMTP secrets.
- **FR-018**: Rejections and failures MUST follow the established conventions (invalid request,
  invalid token or credential, service unavailable) without exposing Identity, persistence, or
  stack details, and Phase 1–5 public behavior MUST be preserved.

### Applicable Non-Functional Requirements

- **NFR-001**: Verification MUST use a small set of consolidated integration scenarios on the
  existing persistent Identity, SQLite, and session test facilities, with a test double only at the
  email-delivery boundary, covering: recovery for existing and nonexistent accounts with equivalent
  responses and the sender invoked only when appropriate; valid, invalid, altered, and (where
  deterministic) expired tokens; password replacement and session revocation; a still-valid token
  after an application restart; key-storage persistence across the Compose lifecycle; and absence
  of the token and SMTP secrets from logs.
- **NFR-002**: Verification MUST NOT depend on arbitrary waits and MUST control time where expiry is
  exercised.
- **NFR-003**: The feature MUST NOT add message queues, background email processing, external email
  services, retry frameworks, a separate token store, templates beyond the recovery message, a
  second database, distributed caches, or additional permanent services.

### Key Entities *(include if feature involves data)*

- **Reset token**: A temporary, Identity-issued, tamper-evident value bound to one account and its
  security state; it is delivered only by email and is neither stored by Authentication API nor
  exposed in any response or log.
- **Key material**: The persistent keys that protect Identity-issued tokens; held in operator
  controlled storage that outlives the container and the Compose project.
- **Recovery message**: The single email that carries the reset token to an existing, enabled
  account's address.
- **Renewable session family**: The Phase 4 revocation boundary; a successful reset revokes all of a
  user's families.

### Scope Exclusions

This feature MUST NOT introduce application-wide rate limiting or rate limiting by email or source
address, reverse-proxy hardening, frontend functionality, multi-factor authentication, email
verification, administrator-initiated password resets, password history or expiry, new
refresh-session infrastructure, JWT blacklists, distributed caches, additional permanent services,
or work belonging to Phases 7 or 8, and MUST NOT leave placeholders for them.

## Traceability

| Feature area | Normative sources |
|---|---|
| Recovery request and anti-enumeration | SRS FR-PWD-001 through FR-PWD-006; Roadmap §12.4 |
| Reset and token validity | SRS FR-PWD-007 through FR-PWD-011; Roadmap §12.5 |
| Session revocation after reset | SRS FR-PWD-012; Roadmap §12.5; Phase 4 revocation rules |
| Email port and delivery | SRS NFR-MAIL-001 through NFR-MAIL-005; Technical Constraints §16; Roadmap §12.2 |
| Persistent Data Protection | SRS NFR-DP-001 through NFR-DP-007; Roadmap §12.3; Technical Constraints data-protection storage sections |
| Logging and secrets | SRS NFR-LOG-001 through NFR-LOG-004; NFR-CONFIG-002, -003; Technical Constraints §15.7 |
| Error conventions | SRS NFR-ERR-001 through NFR-ERR-003 and §34.1 |
| Stateless access tokens | SRS FR-LOGOUT-005 through -007; Roadmap Phase 4 (DEC-006) |
| Verification and gate | Roadmap §12.6 and §12.7 (Gate G6); Constitution Principles I, II, VI, VII |

**Phase-boundary note**: SRS NFR-SEC-BF-006 through -009 and TEST-045 name forgot-password among the
anonymous endpoints subject to rate limiting (`429`), whereas the roadmap assigns general
rate-limiting policies to Phase 7 and does not list them in Phase 6. This is a timing conflict in
the baseline, not a waiver: the requirement stays in force and is implemented in Phase 7, with no
baseline amendment and no placeholder here.

**Dependency note**: Gate G5 is recorded as closed in the roadmap (approved 2026-10-08, Phase 5
closing commit merged to `main`), so its prerequisite does not block this specification.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every verified recovery request, the external response is identical for existing,
  nonexistent, and disabled accounts, an email is requested only for an existing enabled account,
  and no response or log contains a reset token.
- **SC-002**: A valid reset token replaces the password in 100% of verified cases, and invalid,
  altered, other-account, expired, and already-used tokens change nothing in 100% of verified cases.
- **SC-003**: After a successful reset, 100% of the user's renewable sessions are unable to refresh,
  while access tokens issued earlier remain subject only to local JWT validation.
- **SC-004**: A still-valid reset token is accepted after a restart, a container recreation, and a
  teardown of the Compose project with its volumes, using only operator-controlled key storage.
- **SC-005**: Gate G6 evidence shows complete forgot and reset flows, decoupled SMTP delivery,
  persistent key storage, effective anti-enumeration, complete session revocation, a passing build
  and tests, and passing Phase 1–5 regression, before the phase is marked complete.

## Assumptions

- Gate G5 is closed (see Dependency note); Identity, SQLite, Phase 4 session revocation, JWT
  validation, and the error conventions are reused unchanged.
- The token lifetime is the one Identity already applies to reset tokens, adjustable only through
  Identity's own configuration; Phase 6 neither defines a default nor adds a new lifetime rule.
- The recovery message carries the token and tells the user to submit it with their email to the
  reset operation; no frontend link is generated because no frontend exists in this project.
- A disabled account is not eligible for recovery: no token or email for it, and a reset for it is
  rejected like an invalid token. Lockout state does not affect recovery and a reset does not alter
  it.
- Anti-enumeration is judged on the externally observable response (status, body, headers); timing
  equivalence is not claimed.
- SMTP settings are required Authentication API settings from this phase, validated at startup like
  the earlier required settings and never echoed; Phase 1–5 acceptance runs supply a disposable
  sender for them.
- Request and response schemas and exact status codes are contract details settled in planning,
  provided they satisfy the behavior above and SRS §34.1.
