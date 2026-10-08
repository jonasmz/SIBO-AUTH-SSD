# Feature Specification: Phase 3 — User and Role Administration

**Feature Branch**: `003-phase-3-user-role-administration`

**Created**: 2026-10-08

**Status**: Draft

**Input**: Phase 3 — Administración de usuarios y roles

## Clarifications

### Session 2026-10-08

- Q: Which user attributes may the update operation modify? → A: Only the email; any other attribute in the request is rejected as an invalid request (enable/disable and roles have their own operations, and password change is Phase 5).
- Q: Does setting a user's roles replace the whole role set or only add? → A: Complete replacement; the user ends with exactly the roles supplied, omitted roles are removed, and an empty list leaves the user with no roles.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrators Manage Users (Priority: P1)

An authenticated administrator creates users, lists them, retrieves one by identifier, and
updates the email of a user. Every administrative operation is available only to
callers holding a valid access token with the `Administrator` role.

**Why this priority**: Without protected user administration there is no way to manage identities
beyond the single built-in administrator, and every later administrative capability builds on the
same access control.

**Independent Test**: With the Phase 1 administrator token, create a user, list users, retrieve the
user, and update its email; then repeat a representative call with no token and with a
valid token lacking the `Administrator` role.

**Acceptance Scenarios**:

1. **Given** a valid administrator token, **When** a user is created with an email, an initial
   password, an enabled state, and optionally existing roles, **Then** the user is created with
   those values and appears in the administrative views with the requested roles.
2. **Given** an existing user, **When** another user is created whose email differs only by letter
   case, **Then** the creation is rejected as a duplicate and nothing is created.
3. **Given** an initial password that does not satisfy the configured password policy, **When** a
   user is created, **Then** the request is rejected without creating the user.
4. **Given** several users exist, **When** the administrator lists users or retrieves one by
   identifier, **Then** each entry shows its identifier, email, enabled state, lockout state, and
   roles, and never any prohibited identity data.
5. **Given** an existing user, **When** the administrator updates the email to a valid unused value,
   **Then** the change is applied and login now uses the new email; **When** the new email belongs
   to another user, **Then** the update is rejected; **When** the request also carries any other
   attribute, **Then** it is rejected as invalid and nothing changes.
6. **Given** no access token, an invalid token, or a valid token without the `Administrator` role,
   **When** any administrative endpoint is called, **Then** the response is `401 Unauthorized` for
   the first two and `403 Forbidden` for the last.
7. **Given** an identifier that does not exist, **When** a user is retrieved or updated, **Then**
   the response is `404 Not Found`.

---

### User Story 2 - Disable and Re-enable Accounts (Priority: P2)

An administrator disables a user so that the account can no longer sign in, and later re-enables
it so that the same credentials work again.

**Why this priority**: Disabling is the supported way to withdraw access without deleting an
identity, and it completes the login contract left open in Phase 1.

**Independent Test**: Create a user, confirm login works, disable the user and confirm login is
refused with the generic credential failure, re-enable the user and confirm login works again with
the unchanged password.

**Acceptance Scenarios**:

1. **Given** an enabled user who can sign in, **When** the administrator disables the account,
   **Then** a login attempt with the correct credentials receives the same generic
   `401 Unauthorized` result as an unknown email or wrong password.
2. **Given** a disabled user, **When** the administrator re-enables the account, **Then** the user
   can sign in with the same password, subject to the existing lockout rules.
3. **Given** a user is disabled and later re-enabled, **When** account attributes are inspected,
   **Then** the password, email, and role assignments are unchanged.
4. **Given** a user created in the disabled state, **When** login is attempted, **Then** it is
   refused exactly as for a disabled account.

---

### User Story 3 - Manage Roles and Assignments (Priority: P3)

An administrator creates, lists, renames, and deletes roles, and sets which roles each user holds.

**Why this priority**: Roles are the authorization model consumed by every API; managing them is
the second half of the phase after users.

**Independent Test**: Create a role, assign it to a user, confirm it appears in the user's roles,
remove it, delete the unassigned role, and confirm the deletion of an assigned role is refused.

**Acceptance Scenarios**:

1. **Given** a valid administrator token, **When** a role is created with a new name, **Then** it
   is created and listed; **When** the normalized name already exists, **Then** the creation is
   rejected as a duplicate.
2. **Given** an existing role and a user, **When** the administrator sets the user's roles to a
   list, **Then** the user holds exactly the roles in that list and a subsequent login issues an
   access token whose role claims match them.
3. **Given** a user holding a role, **When** the administrator sets the user's roles to a list
   that omits it, **Then** the role is removed from that user and from nobody else.
4. **Given** a role assigned to no user, **When** it is deleted, **Then** it no longer exists;
   **When** the role is assigned to at least one user, **Then** the deletion is refused and nothing
   changes.
5. **Given** a role with assigned users, **When** it is renamed to an unused name, **Then** the
   assignments remain and newly issued tokens carry the new name; **When** the new name collides
   with another role after normalization, **Then** the rename is rejected.
6. **Given** a role set that references a role that does not exist, **When** it is assigned to a
   user, **Then** the request is rejected without applying any part of it.
7. **Given** an access token issued before a role change, **When** it is presented to a consumer
   API, **Then** it is treated according to the roles it carried when issued.

---

### User Story 4 - The System Always Keeps an Enabled Administrator (Priority: P4)

No administrative operation, from any administrator account and by any route, can leave the system
without an enabled user who holds the `Administrator` role, and the canonical `Administrator` role
cannot be removed from the system.

**Why this priority**: This protects the system against locking every administrator out, which
would require manual database intervention to recover.

**Independent Test**: With only the initial administrator enabled, attempt to disable it, to remove
its `Administrator` role, and to rename or delete the `Administrator` role, and confirm each is
refused and leaves the state unchanged; then add a second enabled administrator and confirm the
first can be disabled.

**Acceptance Scenarios**:

1. **Given** exactly one enabled user holds the `Administrator` role, **When** any administrator
   disables that user, **Then** the operation is refused with a state-conflict outcome and the user
   remains enabled.
2. **Given** exactly one enabled user holds the `Administrator` role, **When** a role update would
   remove that role from that user, **Then** the operation is refused and the roles are unchanged.
3. **Given** the `Administrator` role, **When** it is deleted or renamed, **Then** the operation is
   refused and the role is unchanged.
4. **Given** two enabled administrators, **When** one is disabled or loses the `Administrator`
   role, **Then** the operation succeeds and exactly one enabled administrator remains.
5. **Given** a disabled administrator, **When** the remaining enabled administrator is disabled or
   loses the role, **Then** the operation is refused, because disabled accounts do not count.

### Edge Cases

- A disabled or role-reduced administrator's already issued access token keeps working, on
  administrative endpoints and on consumer APIs, until it expires; there is no immediate
  revocation (consistent with the project's residual-validity model).
- Lockout after failed passwords is a temporary Identity state and is independent of the enabled
  state; a locked-out account is still an enabled account for the last-administrator rule.
- Updating a user's email to the value it already has is not a conflict with itself.
- Email and role names that differ only by letter case are the same value for uniqueness.
- A request that targets the acting administrator's own account (disable, role removal) is subject
  to the same rules as any other account.
- Two concurrent administrative operations must not jointly leave the system without an enabled
  administrator even if each would be allowed alone.
- Disabling an already disabled user, or enabling an already enabled user, is accepted and leaves
  the account unchanged.
- A user may hold no roles, one role, or several roles; creating or updating with an empty role
  set is valid for non-protected accounts.
- A malformed request, an unknown field, or an empty update is rejected as an invalid request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every `/api/admin/*` operation MUST require a valid access token and the
  `Administrator` role. A request without valid authentication MUST receive `401 Unauthorized`;
  an authenticated request without the role MUST receive `403 Forbidden`. The two outcomes MUST be
  distinguishable.
- **FR-002**: Administrative authentication MUST accept exactly the tokens the Phase 2 consumer APIs
  accept — same signature algorithm, issuer, audience, and expiry rule with the same explicit
  clock tolerance — and MUST decide authorization from the claims carried in the token.
- **FR-003**: An administrator MUST be able to create a user by supplying an email, an initial
  password, an enabled state, and optionally existing roles. When the enabled state is omitted the
  user is enabled. A successful creation MUST return the created user.
- **FR-004**: The system MUST reject a user whose normalized email already exists, using the same
  email normalization as login, and MUST reject an initial password that does not satisfy the
  configured password policy. A rejected creation MUST NOT leave a partial user or partial role
  assignment.
- **FR-005**: An administrator MUST be able to list users and to retrieve a user by identifier. Each
  user view MUST show the identifier, email, enabled state, lockout state, and assigned roles. An
  unknown identifier MUST yield `404 Not Found`.
- **FR-006**: No administrative response MUST ever contain a password or password hash, a security
  stamp, a token, a cryptographic key, or any other prohibited identity data.
- **FR-007**: An administrator MUST be able to update a user's email, and ONLY the email; any other
  attribute in an update request (including the enabled state, roles, and password) MUST cause the
  request to be rejected as an invalid request. Updates MUST validate email uniqueness after
  normalization and MUST NOT conflict with the user's own current email.
- **FR-008**: The system MUST NOT provide physical deletion of users; withdrawing access is done by
  disabling.
- **FR-009**: An administrator MUST be able to disable and to enable a user. A disabled user MUST
  NOT obtain a new access token through login, and the refusal MUST be externally equivalent to the
  generic credential failure for an unknown email, wrong password, or locked account — same status,
  same body shape, and no practical timing distinction.
- **FR-010**: A re-enabled user MUST be able to sign in again with the unchanged password, subject
  to the existing Identity security rules such as lockout.
- **FR-011**: Enabling or disabling a user MUST NOT change the user's password, email, role
  assignments, or other unrelated attributes.
- **FR-012**: An administrator MUST be able to list roles and to create roles. Role names MUST be
  unique after normalization, and a duplicate MUST be rejected with a state-conflict outcome.
- **FR-013**: An administrator MUST be able to rename a role to a name that is unused after
  normalization. Users holding the role MUST keep it under its new name, and a colliding name MUST
  be rejected.
- **FR-014**: An administrator MUST be able to delete a role only when it is assigned to no user;
  deleting an assigned role MUST be refused and change nothing. An unknown role MUST yield
  `404 Not Found`.
- **FR-015**: An administrator MUST be able to set the roles a user holds. The operation replaces the
  user's complete role set: the user ends with exactly the roles supplied, omitted roles are
  removed, and an empty list leaves the user with no roles. Only roles that exist may be supplied;
  a reference to a nonexistent role MUST cause the whole operation to be rejected as an invalid
  request with no part applied, and an unknown user MUST yield `404 Not Found`.
- **FR-016**: The system MUST NOT allow the last enabled administrator — the only enabled user
  holding the `Administrator` role — to be disabled, nor to lose the `Administrator` role; each
  refused operation MUST leave the state unchanged and return a state-conflict outcome. Disabled
  users do not count as administrators.
- **FR-017**: The canonical `Administrator` role MUST NOT be deleted or renamed while the system
  depends on it for administration, and no role operation may bypass FR-016.
- **FR-018**: The invariants in FR-016 and FR-017 MUST hold regardless of which administrator
  account performs the operation, including operations on the acting account, by any operation or
  combination of operations, and under concurrent administrative requests.
- **FR-019**: Newly issued access tokens MUST reflect the user's roles at the time of issuance.
  Changes to roles or to the enabled state MUST NOT alter access tokens already issued and MUST NOT
  introduce token blacklists, remote validation, or session state.
- **FR-020**: The Phase 1 login request and response contract, the access-token claims, and the
  Phase 2 consumer behavior MUST remain unchanged and verified.
- **FR-021**: Error responses MUST use a consistent problem structure with the baseline status codes
  — invalid request `400`, unauthenticated `401`, forbidden `403`, missing resource `404`, and
  duplicate or state conflict `409` — and MUST NOT expose stack traces or Entity Framework, SQLite,
  or Identity internals.

### Applicable Non-Functional Requirements

- **NFR-001**: Phase 3 verification MUST use a small, high-value set of consolidated automated
  scenarios plus the existing deployment demonstration for current and previous capabilities,
  exercising real identity behavior rather than simulated substitutes, and MUST NOT rely on
  future-phase capabilities, placeholder tests, or arbitrary coverage targets.
- **NFR-002**: Administrative requests and responses MUST NOT cause passwords, access tokens, or key
  material to be written to logs.
- **NFR-003**: The generic login failure for a disabled account MUST remain indistinguishable from
  the other credential failures, preserving the anti-enumeration guarantee.

### Key Entities *(include if feature involves data)*

- **Managed user**: An identity visible to administrators with identifier, email, enabled state,
  lockout state, and assigned roles. The enabled state is new to the administrative view and
  determines whether login is possible; it is separate from temporary lockout.
- **Role**: A named permission group with an identifier and a normalized-unique name; may be
  assigned to zero or more users. `Administrator` is the canonical, protected role.
- **Role assignment**: The relation between a user and a role; the set of roles held by a user
  determines the role claims of newly issued access tokens.
- **Enabled administrator**: An enabled user who holds the `Administrator` role; the system must
  always contain at least one.

### Scope Exclusions

This feature MUST NOT introduce:

- Refresh tokens, refresh-token families, renewable sessions, session persistence or revocation,
  stateful logout, or revoking sessions when a user is disabled (Roadmap Phase 4).
- Automatic invalidation of previously issued access tokens, JWT blacklists, introspection, or any
  remote token validation.
- Password change, forgot-password or reset-password flows, email delivery, or SMTP.
- Physical deletion of users or any administrative endpoint beyond the eleven listed in the
  baseline contract for Phase 3.
- New business functionality in the consumer APIs, changes to the consumer project or its
  architecture, new services, new storage, or infrastructure required only by later phases.
- Approval workflows, recovery services, or administrative hierarchies beyond the single
  `Administrator` role.
- Placeholder interfaces, entities, tables, or methods for any later-phase capability.
- Structured administrative event logging and observability work assigned to Roadmap Phase 8.

## Traceability

| Feature area | Normative sources |
|---|---|
| Administrative access control | SRS FR-AUTHZ-001 through FR-AUTHZ-005; §18 preamble; Roadmap §9.2 Autorización |
| User creation and validation | SRS FR-USER-001 through FR-USER-003; FR-ID-002, FR-ID-004; NFR-SEC-PWD-003; UC-06; TEST-031, TEST-032 |
| User views and data exposure | SRS FR-USER-004, FR-USER-005; NFR-ERR-001 through NFR-ERR-003 |
| User update | SRS FR-USER-006, FR-USER-007 |
| Disable and enable | SRS FR-USER-008, FR-USER-011, FR-USER-012, FR-USER-015, FR-USER-016; FR-LOGIN-007, FR-LOGIN-012, FR-LOGIN-013, FR-LOGIN-015; NFR-SEC-ENUM-001, NFR-SEC-ENUM-004; UC-07 steps 1–3; Roadmap §9.2 Disable user |
| Role administration | SRS FR-ROLE-004 through FR-ROLE-009 |
| Role assignment and token reflection | SRS FR-ROLE-010 through FR-ROLE-013; Phase 1 access-token contract |
| Last enabled administrator | SRS FR-ROLE-014, FR-ROLE-015, FR-USER-012; TEST-036, TEST-037 |
| Error conventions | SRS §34.1 |
| Endpoint surface | SRS §35.2 and §35.3 minus the Phase 4 session-revocation endpoint |
| Phase boundary and verification | Constitution Principles I, II, VI, and VII; Roadmap RD-001 through RD-008, §9.3 through §9.5; SRS TEST-033 through TEST-035 |

Deferred by sequencing, not contradicted: SRS FR-USER-009, FR-USER-010, FR-USER-011 (as to
refresh families), FR-USER-013, FR-USER-014 and UC-07 steps 4–5 (session revocation and event
recording) belong to Roadmap Phase 4 and Phase 8. In this phase, disabling prevents new logins
only.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create, list, retrieve, and update users, and each result shows
  only the permitted fields; 100% of tested duplicate-email and password-policy violations are
  rejected without creating or altering a user.
- **SC-002**: A disabled user is refused at login with a response indistinguishable from an unknown
  email or wrong password, and the same user signs in again after being re-enabled with an
  unchanged password.
- **SC-003**: An administrator can create, rename, assign, remove, and delete roles under the
  stated rules; 100% of tested duplicate-name, nonexistent-role, and assigned-role-deletion cases
  are rejected with no state change.
- **SC-004**: Anonymous requests receive `401`, authenticated non-administrators receive `403`,
  and administrators are authorized on all eleven administrative operations.
- **SC-005**: With a single enabled administrator, 100% of tested attempts to disable it, remove
  its `Administrator` role, or delete or rename the `Administrator` role are refused and leave the
  state unchanged; with two enabled administrators, one can be disabled.
- **SC-006**: A newly issued access token carries the user's current roles, while a previously
  issued token keeps the claims it was issued with, and Phase 2 consumer APIs continue to accept
  and reject tokens exactly as before.
- **SC-007**: Gate G3 evidence shows build, focused automated verification, the administrative
  workflow, and Phase 1–2 regression checks passing before Phase 3 is marked complete.

## Assumptions

- Phases 1 and 2 are complete with Gates G1 and G2 approved; the Phase 1 login contract
  (generic `401` for every credential failure), the access-token contract, and the Phase 2
  consumer behavior are the fixed baseline for this phase.
- The existing credential rules, email normalization, lockout policy, and configurable password
  policy are reused unchanged; Phase 3 adds no second account store.
- The enabled state defaults to enabled when omitted at creation, and enable/disable operations are
  idempotent.
- "Lockout state" in user views means whether the account is currently locked out and until when;
  it is separate from the enabled state, and failed-attempt counters are not exposed.
- Listing returns all users and all roles; the installation is small, and no paging or filtering is
  required by the baseline.
- Roles referenced by an operation exist or the operation is invalid; the exact wire representation
  of a role reference (identifier or name) is defined in the design contract.
- Administrative endpoints live in Authentication API; the reference consumer project authorized by
  DEC-009 and amendment 1.1 of Technical Constraints §5.2 is unchanged and is used only for Phase 2
  regression.
- Authentication API gains the ability to validate access tokens it issues, using the same
  validation policy as the consumer APIs, without calling any other service.
- A disabled or demoted administrator's earlier tokens remain valid until expiry; immediate
  revocation is out of scope by project decision.
