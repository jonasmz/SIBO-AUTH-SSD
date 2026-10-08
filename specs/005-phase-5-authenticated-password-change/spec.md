# Feature Specification: Phase 5 — Authenticated Password Change

**Feature Branch**: `005-phase-5-authenticated-password-change`  
**Created**: 2026-10-08  
**Status**: Draft  
**Input**: Phase 5 — Authenticated Password Change

## Clarifications

### Session 2026-10-08

- Q: Which browser session is kept as "the current session" when revoking the user's other sessions? → A: The family identified by the `auth_refresh` cookie sent with the request, if usable and owned by the same user; all other families are revoked. With no usable cookie, all of the user's families are revoked.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authenticated Password Change (Priority: P1)

An authenticated user replaces their own password by supplying the current password and a valid
new one, without any email-based recovery, reset token, administrator approval, or additional
authentication challenge.

**Why this priority**: This is the capability of the phase; every other story depends on it.

**Independent Test**: Sign in, call the change operation with the current and a new password, then
verify the old password no longer signs in and the new one does.

**Acceptance Scenarios**:

1. **Given** a request without a valid access token, **When** the change operation is called,
   **Then** it is rejected with the established authentication-required (`401`) behavior and no
   password changes.
2. **Given** an authenticated user, **When** they supply an incorrect current password, **Then**
   the change is rejected, the password is unchanged, and no password value is revealed.
3. **Given** an authenticated user and a new password that violates the existing Identity password
   policy, **When** the change is requested, **Then** it is rejected with the established
   validation-error convention and the password is unchanged.
4. **Given** an authenticated user (of any role) who supplies the correct current password and a
   valid new password, **When** the change is requested, **Then** it succeeds, the previous
   password no longer authenticates, and the new password authenticates through the existing login.
5. **Given** a successful change, **When** the account is inspected, **Then** its email, roles,
   enabled state, and other attributes unrelated to the password are unchanged.

---

### User Story 2 - Other Sessions Stop Renewing After a Password Change (Priority: P1)

After a successful change, the user's other renewable browser sessions can no longer obtain new
access tokens, so a session established with the old password cannot outlive it.

**Why this priority**: Revoking other sessions is the security purpose of changing a password and
is a mandatory Gate G5 outcome.

**Independent Test**: Establish two renewable sessions for one user, change the password, and
verify the session that was not used for the change can no longer refresh.

**Acceptance Scenarios**:

1. **Given** a user with more than one active renewable-session family, **When** they change their
   password successfully, **Then** every active family other than the current session can no longer
   refresh.
2. **Given** a failed change (unauthenticated, incorrect current password, or invalid new
   password), **When** the user's sessions are inspected, **Then** none of them is revoked.
3. **Given** an access token issued before the change, **When** it is presented to the Business
   APIs before its natural expiry, **Then** it remains valid under the unchanged local JWT
   validation; no blacklist or remote check is introduced.
4. **Given** the change request carries a usable refresh cookie belonging to the same user,
   **When** the change succeeds, **Then** the family it identifies remains active and can still
   refresh, while all other families are revoked.
5. **Given** the change request carries no usable refresh cookie (absent, malformed, unknown,
   expired, revoked, or owned by another user), **When** the change succeeds, **Then** every active
   family of the user is revoked.

---

### User Story 3 - Initial Administrator Retires the Default Password (Priority: P1)

The built-in administrator replaces the initial `admin` password through the same change operation,
without a working email address, and the new password survives restarts.

**Why this priority**: Closing the default-credential cycle is the stated objective of the phase
and a mandatory Gate G5 criterion.

**Independent Test**: Sign in as the built-in administrator with the initial password, change it,
restart Authentication API, and verify only the new password signs in.

**Acceptance Scenarios**:

1. **Given** the built-in administrator signed in with the initial password, **When** they change
   it to a valid new password, **Then** the initial password no longer authenticates and the new one
   does, with no email sent or required.
2. **Given** the changed password, **When** Authentication API restarts (including database
   initialization), **Then** the new password still authenticates and the initial password is not
   restored.
3. **Given** the built-in administrator is the only enabled Administrator, **When** they change
   their password, **Then** their enabled state and role are unaffected, so the Phase 3
   last-administrator protection is neither triggered nor weakened.

### Edge Cases

- A new password identical to the current password is judged only by the existing Identity policy;
  Phase 5 adds no password-history or reuse rule.
- A request missing the current or the new password, or carrying a malformed or empty body,
  follows the established invalid-request convention and changes nothing.
- Two simultaneous change requests for one account must leave exactly one coherent final password
  and must not leave a revoked-session state that contradicts it.
- A successful change followed by failure to complete session revocation must not be reported as
  success while other sessions remain renewable.
- A persistence failure during the change yields the established service-unavailable convention and
  leaves the previous password and sessions unchanged.
- Passwords, password hashes, security stamps, and Identity internals never appear in responses or
  logs, including failure responses.
- A user whose access token remains valid after being disabled or locked is not given additional
  checks by this phase beyond those stated in the requirements.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Authentication API MUST provide `POST /api/auth/change-password`, which requires a
  valid access token and is available to any authenticated user regardless of role.
- **FR-002**: The operation MUST act only on the account identified by the presented access token;
  it MUST NOT accept or act on any other account identifier.
- **FR-003**: The request MUST contain both the current password and the new password; a request
  lacking either, or otherwise invalid, MUST be rejected without changing the password.
- **FR-004**: The current password MUST be verified through the existing Identity mechanism; an
  incorrect current password MUST be rejected without changing the password.
- **FR-005**: The new password MUST satisfy the existing, externally configured Identity password
  policy; Phase 5 MUST NOT define a different or additional policy.
- **FR-006**: A successful change MUST replace the stored credential using Identity's supported
  mechanism so that the previous password no longer authenticates, the new password authenticates
  through the existing login, and no other account data changes.
- **FR-007**: No additional reauthentication, email verification, reset token, or administrator
  approval MUST be required beyond a valid access token and the current-password check.
- **FR-008**: After a successful change, every active renewable-session family of the user other
  than the current session MUST be revoked and MUST NOT subsequently refresh, using the Phase 4
  revocation rules. The current session is the family identified by the `auth_refresh` cookie sent
  with the request when that credential is usable and belongs to the same user; it remains active.
  When no such usable cookie accompanies the request, every active family of the user MUST be
  revoked.
- **FR-009**: A failed change MUST NOT revoke any session and MUST NOT alter the credential.
- **FR-010**: The change and the resulting session revocation MUST be observed as one outcome: a
  success response MUST NOT be returned while any session that should have been revoked can still
  renew.
- **FR-011**: The operation MUST NOT introduce an access-token blacklist, remote token validation,
  token introspection, or any change to how Business API A and Business API B validate JWTs;
  previously issued access tokens MAY remain valid until natural expiry.
- **FR-012**: The built-in administrator MUST be able to replace the initial `admin` password
  through this same operation without any dependency on email or SMTP, with no administrator-only
  password logic.
- **FR-013**: A changed password MUST persist across restarts; startup and database initialization
  MUST NOT restore the initial `admin` password or any previous password.
- **FR-014**: Rejections and failures MUST follow the established conventions: `401` for missing or
  invalid access tokens, the invalid-request convention for validation failures, and the generic
  service-unavailable convention for persistence unavailability, without exposing passwords,
  hashes, security stamps, or Identity internals.
- **FR-015**: A successful password change and its session revocation MUST be recorded as a
  security event through the existing logging facilities with UTC time and correlation where
  available, and logs MUST NOT contain passwords, hashes, security stamps, tokens, or refresh
  credentials.
- **FR-016**: The phase MUST preserve Phase 1–4 public behavior except for the new operation, and
  MUST NOT introduce forgot-password, reset-password, email delivery, reset tokens, email
  verification, multi-factor authentication, administrator-initiated resets, password history or
  expiry, additional session-management endpoints, or future-phase abstractions.
- **FR-017**: The refresh cookie MUST be used only to identify which family to keep; it MUST NOT
  authenticate or authorize the request, no cookie-dependent state is changed by presenting it, and
  no session identifier or additional access-token claim is introduced.

### Applicable Non-Functional Requirements

- **NFR-001**: Verification MUST use a small set of consolidated scenarios on the existing
  persistent Identity and session test facilities, covering: authentication required, incorrect
  current password rejected, invalid new password rejected, successful change, old password no
  longer authenticates, new password authenticates, other sessions revoked, administrator change
  without email, restart not restoring `admin`, and Phase 1–4 regression.
- **NFR-002**: Verification MUST NOT depend on arbitrary waits and MUST control time where
  time-dependent behavior is exercised.
- **NFR-003**: The feature MUST NOT add SMTP, `IEmailSender`, reset or recovery tokens, a second
  database, new permanent services, or consumer-API access to credential or session data.

### Key Entities *(include if feature involves data)*

- **User credential**: The user's password as held by Identity; replaced, never exposed, by this
  feature.
- **Renewable session family**: The Phase 4 revocation boundary for a user's browser sessions; its
  revocation reasons are extended only as needed to record that a password change ended it.

### Scope Exclusions

This feature MUST NOT introduce `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`,
SMTP or email delivery, `IEmailSender`, password-reset tokens, email-verification workflows,
multi-factor authentication, administrator-initiated resets, password history or expiry policies,
session listing or other session-management endpoints, distributed or centralized JWT revocation,
new consumer-API functionality, or work belonging to Phases 6–8.

## Traceability

| Feature area | Normative sources |
|---|---|
| Endpoint, authentication, current and new password | SRS FR-CHANGE-PWD-001, -002, -005; Roadmap §11.2 |
| Password policy | SRS FR-CHANGE-PWD-003; existing Identity configuration (Phase 1) |
| Revocation of other sessions | SRS FR-CHANGE-PWD-004; FR-REFRESH-007, -008; Roadmap §11.2 |
| Initial administrator | SRS FR-ADMIN-BOOT-003; NFR-SEC-ADMIN-001 through -003; Roadmap §11.3 |
| Stateless access tokens | SRS FR-LOGOUT-005 through -007; Roadmap Phase 4 (DEC-006) |
| Scope exclusions | Roadmap §11.4; Phase 6 scope |
| Verification and gate | Roadmap §11.5 and §11.6 (Gate G5); Constitution Principles I, II, VI, VII |

**Dependency note**: Gate G4 is recorded as closed in the roadmap (approved 2026-10-08, Phase 4
closing commit merged to `main`), so its prerequisite does not block this specification.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the verified lifecycle, 100% of unauthenticated requests, incorrect-current-password
  requests, and policy-violating new passwords are rejected with no change to the credential or to
  any session.
- **SC-002**: After a successful change, the previous password fails to sign in and the new one
  succeeds in 100% of verified cases, for both an ordinary user and the built-in administrator.
- **SC-003**: After a successful change, 100% of the user's other active renewable sessions are
  unable to refresh, while access tokens issued earlier remain subject only to local JWT validation.
- **SC-004**: After restarting Authentication API, the built-in administrator signs in with the
  replaced password and never with the initial `admin` password, with no email involved.
- **SC-005**: Gate G5 evidence shows the complete change operation, replacement of the initial
  administrator password, integrated session revocation, no email or recovery infrastructure, a
  passing build and tests, and passing Phase 1–4 regression, before the phase is marked complete.

## Assumptions

- Gate G4 is closed (see Dependency note); Phase 4 refresh sessions, family revocation, and the
  `Origin`/cookie conventions are reused unchanged.
- The "existing Identity password policy" is the policy already configured and applied at user
  creation and by any other password-accepting operation; it may be weak by design (the initial
  `admin` password is deliberately weak) and Phase 5 does not strengthen it.
- Incorrect-current-password failures follow the established client-error problem-details
  convention; the exact status code and body are contract details settled in planning.
- Failed current-password attempts follow whatever Identity lockout behavior is already
  established; Phase 5 adds no new throttling, which belongs to Phase 7.
- Request and response schemas are design-contract details decided in planning, provided they
  satisfy the behavior above.
