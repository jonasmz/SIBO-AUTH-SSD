# Phase 0 Research: Refresh Tokens, Renewable Sessions and Logout

All planning questions are resolved below. There are no remaining `NEEDS CLARIFICATION` items.

## 1. Persisted session shape

**Decision**: Persist a `RenewableSessionFamily` row and a chain of `RefreshCredential` rows in the
existing `AuthenticationDbContext`. The family is the user-owned absolute-expiry and revocation
boundary. Each credential records its verifier, creation/expiry, consumption/revocation timestamps
and optional replacement.

**Rationale**: Family-wide replay/logout/admin/disable revocation is a first-class requirement, as
are creation, expiry and revocation state for both family and credential. Separate rows make those
invariants explicit and allow one irreversible family update to invalidate every chain member.
(Spec FR-003/009/012/014–016; SRS FR-REFRESH-005–012; Roadmap §10.2–10.8.)

**Alternatives considered**: A token-only table with repeated `FamilyId` and revocation updates was
rejected because the family has its own required lifecycle and concurrent family revocation would
touch an unbounded chain. A second session database was rejected by NFR-004 and CR-DATA-001–005.

## 2. Raw credential and persisted verifier

**Decision**: Generate 32 random bytes with `RandomNumberGenerator`, encode them as unpadded
base64url for the cookie, and persist only a 32-byte SHA-256 digest with a unique index. Reject
malformed or unreasonable cookie values before hashing/lookup. Never log the raw value or digest.

**Rationale**: A uniformly random 256-bit opaque value has enough entropy that a deterministic
digest is safe for indexed lookup without a per-token salt or another application secret. The
baseline explicitly requires `RandomNumberGenerator`; SHA-256 provides the required irreversible
verification representation and uses the shared framework only. (Spec FR-002/017; SRS
FR-REFRESH-001–004, NFR-SEC-TOK-002; Technical Constraints §10.5.)

**Alternatives considered**: Plaintext was prohibited. Password hashing was rejected because the
token is already high entropy and slow verification would prevent direct indexed lookup. HMAC was
not selected because it adds key lifecycle/configuration not required by the baseline. JWT refresh
tokens were prohibited.

## 3. Absolute lifetime

**Decision**: Add external `RefreshSession:LifetimeDays`, defaulting to `7`, and validate at startup
that the value is positive and can be converted and added to the current instant without overflow.
No policy maximum is invented. At login, set the family and initial credential to one absolute UTC
expiry. Every replacement inherits that exact expiry and does not extend it.

**Rationale**: This implements a configurable absolute lifetime and makes the prohibition on sliding
or independent session lifetime observable. `TimeProvider.GetUtcNow()` supplies all timestamps and
deterministic tests. (Spec FR-003/004; SRS FR-REFRESH-009/010, NFR-TIME-001/002; Technical
Constraints §11.)

**Alternatives considered**: Resetting expiry on rotation is sliding expiration and was rejected.
Separate credential and family lifetime settings were rejected as unauthorized scope.

## 4. Atomic rotation and replay handling

**Decision**: Execute refresh in a short `IsolationLevel.Serializable` SQLite transaction, which
acquires the database write lock before session mutation. Load by unique token digest, verify the
family, credential and current Identity user state, mark the credential consumed, insert exactly one
replacement and commit before returning credentials. A later/competing transaction that finds that
digest already consumed revokes the family and returns the same invalid-refresh outcome. Logout,
disablement and administrative revocation use transactions against the same family state.

**Rationale**: SQLite serializes these writers, so two requests cannot both observe and consume a
current token or create independent continuations. Persisted family revocation wins against all
later refresh attempts. The transaction contains no remote I/O. (Spec FR-006–010/012/014/015 and
edge cases; Roadmap §10.5–10.8; Technical Constraints §§8–9.)

**Alternatives considered**: An in-memory lock fails across restarts/processes and is unnecessary.
Optimistic rowversion was rejected because SQLite has no native auto-updating rowversion. A
distributed lock/Redis is excluded. Returning a grace period for concurrent reuse would violate
strict rotation and replay-family revocation.

## 5. Layering and login orchestration

**Decision**: Put family/credential invariants in Domain, use focused session issuance/rotation/
revocation ports and outcomes in Application, implement them with EF/Identity/cryptography in
Infrastructure, and keep cookie/Origin/HTTP mapping in API. Extend the login handler so a successful
Identity validation creates the renewable session before the endpoint emits either token.

**Rationale**: This preserves dependency direction and ensures failed login creates no session and
session-persistence failure cannot return a half-established successful response. JWT issuance
continues through the existing application port. (Spec FR-001/006/007/018; Constitution III.)

**Alternatives considered**: Creating the refresh session in the endpoint would mix persistence and
security workflow into the HTTP boundary. Putting cookie types in Application would violate the
framework boundary. A generic repository/unit of work adds indirection without a current need.

## 6. Cookie transport and request-origin defense

**Decision**: Use cookie `auth_refresh`, `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, no Domain,
and an absolute expiry equal to the family expiry. Set `Secure=true` in Production and allow false
only for local non-TLS Development/Test. Refresh and logout require an `Origin` header whose parsed
scheme/host/effective-port exactly equals required `Security:FrontendOrigin`; absent, malformed,
opaque (`null`) or mismatched origins return generic `403`. Do not enable CORS. Logout always emits
the matching expired cookie even without a usable credential.

**Rationale**: The cookie is inaccessible to JavaScript, limited to authentication paths and not
sent cross-site under normal browser rules. Exact Origin validation supplies the required explicit
CSRF defense for cookie-backed mutations in the defined same-origin deployment. (Spec
FR-005/011/012; SRS FR-REFRESH-013–016, NFR-CSRF-001–004, NFR-CORS-001–003.)

**Alternatives considered**: `__Host-` was not selected because its mandatory Secure attribute
would make the current HTTP-only local test topology unusable. Double-submit tokens need
JavaScript-visible state and add no value over mandatory exact-Origin checks here. Permissive or
credentialed CORS is unnecessary in the same-origin model. Referer fallback was rejected because
the contract requires the authoritative configured origin.

## 7. HTTP contracts and non-revealing errors

**Decision**: Login and refresh return the existing `{ accessToken, expiresAtUtc }` JSON shape;
their refresh credential exists only in `Set-Cookie`. Every unusable refresh token/user/family state
returns one generic `401` ProblemDetails response. Logout returns `204` for every credential state.
Administrative all-family revocation returns `204`, existing `401`/`403`, and `404` for unknown user.
Origin failures return generic `403` before cookie processing.

**Rationale**: This preserves the Phase 1 response while preventing token-state and user-state
enumeration. `204` expresses idempotent mutation without inventing a response body. ProblemDetails
remains the error representation. (Spec FR-007/008/012/014/018; SRS
FR-REFRESH-ENDPOINT-001–005, FR-LOGOUT-001–007; Technical Constraints §18.)

**Alternatives considered**: Replay-specific, expired-specific or disabled-user responses leak
security state. Returning refresh material in JSON violates the browser-only contract. Requiring an
access JWT for refresh conflicts with the endpoint requirements.

## 8. Administrative and disablement revocation

**Decision**: Add all-family revocation to the existing user administration adapter and protected
route group. Disabling updates `IsEnabled` and revokes active families inside the same existing
serializable transaction, after the last-enabled-Administrator guard passes. Enabling never changes
family state. Direct administrative revocation verifies the user first, then stamps every active
family with one UTC revocation time.

**Rationale**: This makes disablement atomic with session withdrawal, preserves the continuity rule,
and ensures a refused disable has no session side effect. Irreversible revocation ensures enablement
cannot revive old sessions. (Spec FR-014/015/018; SRS FR-USER-008–014; Roadmap §10.8.)

**Alternatives considered**: Revoking after commit creates a renewal race and partial outcome.
Deleting token rows loses replay evidence and is unnecessary. Updating credentials individually is
less direct than revoking the family boundary.

## 9. Security event logging

**Decision**: Use `ILogger<T>` structured events for replay detection, logout and administrative or
disablement revocation. Include UTC event time, action/reason, user/family identifiers where useful,
and ambient `Activity`/request correlation already available; never include cookies, access tokens,
hashes, passwords, keys or configuration secrets.

**Rationale**: This meets the required audit evidence with existing logging facilities and no new
subsystem. Family/user identifiers support investigation without exposing credential material.
(Spec FR-017; SRS NFR-LOG-001–005; Technical Constraints logging rules.)

**Alternatives considered**: A new audit database/service is excluded and disproportionate.
Logging token prefixes or hashes was rejected because hashes are explicitly secret in this feature.

## 10. Verification strategy

**Decision**: Unit-test pure family state rules. Integration-test HTTP, Identity, cookie flags,
generic errors, origin validation, real SQLite transactions and deterministic expiry. Use a
temporary file database for migration/restart tests and a coordinated pair of HTTP requests for
same-token concurrency. Add one consolidated Phase 4 Compose script for lifecycle, host persistence,
local consumer validation, log-secret scanning and Phase 1–3 regression.

**Rationale**: These are the smallest test layers that preserve the relevant database, concurrency,
Identity and browser-boundary behavior. (Spec NFR-001–003; Roadmap §10.9/G4; Technical Constraints
§§22–23.)

**Alternatives considered**: EF InMemory cannot prove SQLite constraints/transactions. Sleeps make
expiry tests nondeterministic. Handwritten substitute session stores cannot prove persistence or
replay behavior. A broad end-to-end matrix would duplicate focused integration coverage.
