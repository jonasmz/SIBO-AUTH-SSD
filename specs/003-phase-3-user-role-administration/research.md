# Research: Phase 3 — User and Role Administration

All decisions below apply only to Roadmap Phase 3 (Gate G3). They derive from the SRS, Technical
Constraints, the Roadmap, the constitution, the active specification and its clarifications, and
the code delivered in Phases 1–2. No `NEEDS CLARIFICATION` remains.

## Enabled State Storage

**Decision**: Introduce `ApplicationUser : IdentityUser<string>` in Infrastructure with one added
property, `bool IsEnabled`. Switch `AuthenticationDbContext`, `AddIdentityCore`, `UserManager`, the
credential validator, and the bootstrap to that type. Add one EF Core migration that creates
`AspNetUsers.IsEnabled INTEGER NOT NULL`, with migration-level default `1` so existing rows,
including the built-in administrator, stay enabled. The model itself declares no database default,
so EF Core always writes the CLR value and a user created disabled is stored disabled.

**Rationale**: SRS FR-USER-008 and FR-LOGIN-007 need a durable enabled state, and SRS §40 lists
`Enabled` on User. Technical Constraints §7 explicitly permits a derived `ApplicationUser` "for
the enabled state." Phase 1 research rejected the column only because no capability used it at the
time; Phase 3 is that capability. A model-level `HasDefaultValue(true)` on a `bool` would make EF
Core treat `false` as "unset" and insert the database default, silently enabling disabled users.

**Alternatives considered**: Encoding disablement as `LockoutEnd = DateTimeOffset.MaxValue`
conflicts with the spec, which makes lockout a temporary state independent of enablement, and is
erased by Identity's own lockout reset. A separate table or a Domain user wrapper adds types and
joins without value (Technical Constraints §5.4).

## Disabled-Account Login Refusal

**Decision**: `IdentityCredentialValidator` keeps its Phase 1 flow and checks `IsEnabled` only
after the normal password verification and failure accounting. A disabled account with the
correct password returns `null`, which yields the existing generic `401`. Its failed-attempt
counter is not reset.

**Rationale**: FR-LOGIN-012/013/015 and NFR-SEC-ENUM-001/004 require the same result and no
practical timing difference. Checking after verification means a disabled account spends the same
hashing work as a wrong password or a valid login. Lockout stays Identity's own mechanism
(FR-LOGIN-005/006). The login request, response, and token contract do not change (FR-020).

**Alternatives considered**: Early return before password verification creates a measurable fast
path. `SignInManager.CanSignInAsync` with a custom `IUserConfirmation` brings in SignInManager for
one boolean.

## Administrative Token Validation in Authentication API

**Decision**: Register `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12, already pinned
centrally and authorized by Technical Constraints §33, in Authentication.Infrastructure. Use the
same `TokenValidationParameters` as `ReferenceConsumer.Api`: RS256 only, signed tokens and
expiration required, issuer, audience, lifetime, and signing key validated, `sub`/`role` claim
types, `MapInboundClaims = false`, `IncludeErrorDetails = false`, and no `Authority` or metadata.
The validation key is the public half of the loaded signing key, held in memory. A new required
setting, `Jwt:ClockSkewSeconds` (0–60), uses the same rule as the consumers. Compose passes it the
same `AUTH_JWT_CLOCK_SKEW_SECONDS` variable.

The private PEM is loaded once by a new `RsaSigningKey` singleton. Both `JwtAccessTokenIssuer`
(signing credentials) and the bearer options (public `RsaSecurityKey`) use it. Startup still fails
fast because the issuer is resolved before initialization.

**Rationale**: FR-002 and FR-AUTHZ-001–005 require the same token acceptance as the consumer APIs
and ASP.NET Core policies over JWT claims. NFR-TIME-003 requires a consistent explicit tolerance.
Constitution V requires validation by Microsoft IdentityModel. Validating against the in-process
key needs no network call and no new key file.

**Alternatives considered**: Sharing the consumer's registration code is not possible, because the
consumer must not reference `Authentication.*` and Authentication API must not reference the
consumer. Duplicating about twenty lines of declarative configuration is cheaper than a new
shared project, which would need a baseline amendment. Mounting the public PEM into Authentication
API too adds configuration for a key it already holds. A code default for the clock skew would
let the two sides drift.

## Authorization Policy and Error Bodies

**Decision**: A named policy `Administrator` requires role `Administrator`. One route group,
`/api/admin`, created in `Features/Administration`, applies `RequireAuthorization("Administrator")`.
Every admin endpoint is mapped inside that group, so it is protected by construction. Bearer events
write ProblemDetails for failures: `OnChallenge` writes `401` with `WWW-Authenticate: Bearer` and a
fixed `"Unauthorized"` problem with no token error detail, and `OnForbidden` writes a fixed `403`
problem. Login stays `AllowAnonymous`.

**Rationale**: FR-001/FR-021 require distinguishable `401`/`403` in the consistent problem
structure. Technical Constraints §6.3 prescribes route groups. Limiting the change to the bearer
events leaves all other responses, including the Phase 1 login and health contracts, unchanged.

**Alternatives considered**: A global `UseStatusCodePages` would add bodies to unrelated empty
responses, such as unmapped routes, and change existing behavior. Per-endpoint `RequireAuthorization`
risks missing an endpoint.

## Use-Case Placement Across Layers

**Decision**:

- **Domain**: `Administration/AdministratorContinuity`, a pure rule. Given whether the target is
  currently an enabled administrator, whether it will remain one, and the current enabled
  administrator count, it decides if the change is permitted.
- **Application**: two slice ports, `Features/Users/IUserAdministration` and
  `Features/Roles/IRoleAdministration`, with their view and command records. A shared typed result,
  `Features/Administration/AdministrationResult<T>` with `AdministrationError`
  (`Invalid`, `NotFound`, `Conflict`), plus a fixed safe detail message, follows Technical
  Constraints §27.
- **Infrastructure**: `Identity/UserAdministration` and `Identity/RoleAdministration` implement
  the ports with `UserManager`, `RoleManager`, and the DbContext. They apply the Domain rule
  inside the transaction.
- **Api**: endpoints call the ports directly.

**Rationale**: Identity owns uniqueness, normalization, password policy, and role storage
(Constitution V). The only rule Identity does not own, administrator continuity, is real domain
behavior, testable without infrastructure, so it goes in Domain. Application handlers would only
forward each call to the port, which Constitution IV rejects as ceremony. Login keeps its handler
because it composes two ports. The two ports match the SRS slices (Technical Constraints §5.5)
and hide Identity/EF from Application (§5.3, §5.7).

**Alternatives considered**: A generic repository or a unit-of-work port (forbidden or
speculative), MediatR-style handlers (forbidden), or putting continuity checks in endpoints, which
would leak a business rule to HTTP.

## Atomicity and Concurrency of Invariants

**Decision**: Every mutating administrative operation runs inside one
`context.Database.BeginTransactionAsync()`. Microsoft.Data.Sqlite opens it as
`BEGIN IMMEDIATE`, which takes SQLite's single write lock before the first read. Inside it, the
adapter reads the state it needs (enabled administrator count, role assignment existence,
uniqueness), applies the Domain rule, performs the Identity writes, and commits. Any refusal rolls
back. Concurrent administrative writers queue on the write lock, with Microsoft.Data.Sqlite's busy
retry up to the command timeout, so each one sees the effects of the previous one. The unique
`EmailIndex` and Identity's role-name index remain as database backstops. A `DbUpdateException`
from them maps to `409`.

**Rationale**: FR-004 and FR-015 require no partial state, and FR-016/FR-018 require the
last-administrator invariant to hold under concurrent requests. Authentication API exclusively
owns its single SQLite file (Constitution VII), so the database write lock is a complete
serialization point without an in-process lock or new infrastructure.

**Alternatives considered**: An in-process `SemaphoreSlim` duplicates what SQLite already
guarantees and would not cover a second process. Optimistic concurrency stamps on every user still
allow the cross-row case, where two different administrators are disabled at once. A
`SERIALIZABLE` emulation is not supported by SQLite.

## Request Validation and Unknown Fields

**Decision**: Admin request records carry
`[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`, so any unknown member,
including `enabled`, `roles`, or `password` on `PATCH /users/{id}`, fails deserialization and
yields `400`. One shared helper, `AdministrationRequests.ReadAsync<T>`, requires a JSON content
type and maps `JsonException` to `null`, as the login endpoint does. Each request type exposes an
`IsValid` shape check: required fields, email syntax via `EmailAddressAttribute`, non-blank role
names, and length at most 256, the Identity column size. Semantic rules stay with their owners:
Identity checks email uniqueness and password policy, the adapter checks role existence, and
Domain checks continuity.

**Rationale**: FR-007 (only email may be updated, other attributes rejected), the edge cases
(malformed, unknown field, or empty update → `400`), and Technical Constraints §17 (small request
types, native validation, no FluentValidation, no duplicated rules).

**Alternatives considered**: `JsonPatchDocument` or `application/merge-patch+json` semantics add
a dependency or complexity for a one-field update. Ignoring unknown fields contradicts the
clarification.

## Role References and User Name

**Decision**: Clients reference roles by **name**, matched after Identity normalization, in
`POST /users` (`roles`) and `PUT /users/{id}/roles` (`roles`). Duplicate names that are equal after
normalization collapse to one. User views list role names. Role views expose `id` and `name`.
`PATCH`/`DELETE /roles/{id}` address roles by identifier. A new user's internal Identity `UserName`
is its generated identifier. It is never exposed and never changes when the email changes.

**Rationale**: Role names are what access tokens carry (FR-ROLE-012) and what administrators read
in user views. The spec delegates the wire choice to the design contract. Using the identifier as
the user name avoids `UserNameIndex` conflicts and keeps user names in sync automatically after
an email change.

**Alternatives considered**: Role identifiers in assignment bodies are stable under rename, but
need a second lookup for every human-driven call. Setting the user name to the email requires a
synchronized update and creates spurious conflicts.

## Error Mapping

**Decision**:

| Condition | Status | Detail (fixed text) |
|---|---|---|
| Malformed JSON, wrong content type, unknown field, missing/invalid field | 400 | `The request is invalid.` |
| Password violates Identity policy | 400 | `The password does not satisfy the password policy.` |
| Referenced role does not exist | 400 | `One or more roles do not exist.` |
| Unknown user or role id | 404 | `The user was not found.` / `The role was not found.` |
| Normalized email or role name already used | 409 | `The email is already in use.` / `The role name is already in use.` |
| Would leave no enabled administrator | 409 | `The operation would leave no enabled administrator.` |
| Rename/delete of `Administrator` | 409 | `The Administrator role cannot be renamed or deleted.` |
| Delete of an assigned role | 409 | `The role is assigned to one or more users.` |

The adapter maps Identity error **codes** (`DuplicateEmail`, `DuplicateRoleName`, `InvalidEmail`,
`Password*`, and others) to `AdministrationError`. Identity descriptions, EF/SQLite messages, and
stack traces never reach the response. Unexpected exceptions remain `500` through the existing
`UseExceptionHandler` + ProblemDetails. A `DbException` during an admin call maps to `503`, the
same as login.

**Rationale**: FR-021, SRS §34.1, NFR-ERR-001–003, Technical Constraints §18/§27.

## Response Shapes and Status Codes

**Decision**: `POST` creations return `201` with `Location` and the created view. `GET`, `PATCH`,
`PUT roles`, `enable`, and `disable` return `200` with the current user/role view. `DELETE` returns
`204`. Enable/disable are idempotent (`200`, unchanged state). Lists return JSON arrays, not paged.
Users are sorted by normalized email and roles by normalized name. The Application view records
are serialized directly; there is no separate API response DTO, because the shapes would be
identical (Constitution IV). The contract tests pin the wire shape.

**Rationale**: SRS §34.1 allows 200/201/204. The spec assumptions say no paging. Returning the
resulting view lets the demonstration show the effect of each operation in one call.

## Lockout State in Views

**Decision**: The user view exposes `isLockedOut` (`LockoutEnd` later than
`TimeProvider.GetUtcNow()`) and `lockoutEndUtc` (only when locked, otherwise `null`). Failed-access
counts, `LockoutEnabled`, security stamp, password hash, concurrency stamp, phone, and
two-factor fields are not exposed.

**Rationale**: FR-USER-004 ("lockout state when pertinent") and FR-USER-005. The spec assumption
fixes the meaning. `TimeProvider` keeps it deterministic in tests (Technical Constraints §11).

## Dependencies

**Decision**: No new package version. Authentication.Infrastructure adds a `PackageReference` to
the already-pinned `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12. OpenAPI/Scalar runtime
packages remain deferred, as in Phase 1. The OpenAPI file in `contracts/` is the planning contract.

**Rationale**: Constitution IV requires each dependency to serve the active phase. JwtBearer is
the authorized validator (Technical Constraints §33) and is now needed by Authentication API.

## Verification Strategy

**Decision**: Extend the two existing test projects. There is no new project and no mocking
library.

- **Unit**: `AdministratorContinuity` truth table. `Jwt:ClockSkewSeconds` range validation in
  Authentication API options.
- **Integration** (real SQLite temp files, real Identity, real JwtBearer, real tokens from login):
  four consolidated scenario classes cover access control and token parity, the user lifecycle,
  roles and assignments, and administrator continuity, including a concurrent double-disable.
  Existing Phase 1 and Phase 2 classes run unchanged as regression.
- **Test support**: `TestTokenMinter` gains a constructor that signs with the Authentication API
  test key, so expired, wrong-issuer, and wrong-audience tokens can be minted with the correct
  signature.
- **Acceptance**: `tests/acceptance/phase-3.sh` drives the admin workflow on disposable Compose
  storage, restarts `auth-api` to show persistence, and ends by running `phase-2.sh`, which runs
  `phase-1.sh`.

**Rationale**: NFR-001, Constitution VI, and Roadmap §9.4/G3. Concurrency is proven against real
SQLite locking rather than simulated.

## Out-of-Phase Items Confirmed Absent

No refresh tokens, session families, revoke-sessions endpoint, stateful logout, JWT blacklist,
password change/reset, email/SMTP, OpenAPI/Scalar runtime, structured admin event logging (Phase 8),
user deletion, paging, or administrative hierarchy. The `revoke-sessions` route from SRS §35.2 is
deferred to Phase 4. The endpoint surface is exactly the eleven Roadmap §9.2 operations.
