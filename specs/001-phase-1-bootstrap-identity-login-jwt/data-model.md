# Data Model: Phase 1 Bootstrap, Identity, Admin, Login and JWT

## Modeling Boundary

Phase 1 has no technology-agnostic Domain entity. ASP.NET Core Identity owns the persisted
identity model inside Infrastructure. The Domain project remains free of artificial user/role
wrappers. Access tokens and readiness state are transient, while RSA material is an external
operational artifact rather than database data.

## Persisted Identity State

### Identity User

Backed by native `IdentityUser<string>` in the Infrastructure persistence model.

| Field | Role and validation |
|---|---|
| `Id` | Primary key; generated once; stable; used as JWT `sub`; never derived from email. The built-in administrator uses a fixed reserved ID so startup can identify it after mutable fields change. |
| `UserName` | Internal username. Initial built-in value is `admin`; never accepted as the HTTP login identifier. |
| `NormalizedUserName` | Identity-normalized username; unique according to the Identity schema. |
| `Email` | Required for the built-in user. Initial value is `admin@local.invalid`; used as the HTTP login identifier. |
| `NormalizedEmail` | Produced by Identity normalization; database-enforced unique index. |
| `EmailConfirmed` | Native Identity field; no Phase 1 email-verification workflow is introduced. |
| `PasswordHash` | Created and verified exclusively by Identity; never returned or logged. |
| `SecurityStamp` | Identity-owned security state; never returned or logged. |
| `ConcurrencyStamp` | Identity-owned optimistic concurrency value. |
| `LockoutEnabled` | Identity-owned capability used by credential checks. |
| `LockoutEnd` | Identity-owned UTC lockout state when present. |
| `AccessFailedCount` | Incremented through Identity for failed password checks and handled by normal Identity behavior after success. |

Native Identity fields not used by Phase 1 MAY remain in the standard schema but are not
promoted into product contracts. Phase 1 adds no custom `Enabled`, `MustChangePassword`, session,
refresh-token, recovery, or device fields.

### Identity Role

Backed by native `IdentityRole<string>`.

| Field | Role and validation |
|---|---|
| `Id` | Primary key. The built-in administrative role uses a fixed reserved ID. |
| `Name` | Initial canonical value `Administrator`. |
| `NormalizedName` | Produced by Identity normalization and unique in the Identity schema. |
| `ConcurrencyStamp` | Identity-owned optimistic concurrency value. |

### User–Role Assignment

Backed by native `IdentityUserRole<string>`.

| Field | Role and validation |
|---|---|
| `UserId` | Foreign key to Identity User. |
| `RoleId` | Foreign key to Identity Role. |

The composite key prevents duplicate assignment. On first creation, the built-in administrator
receives exactly the built-in administrative-role assignment.

### Migration History

EF Core migration history records which repository-shipped migrations have been applied. It is
Infrastructure metadata and not an external product entity. The Phase 1 application schema
contains Identity storage and migration history only. Standard tables produced by Identity EF
stores remain framework infrastructure and MUST NOT be used to introduce future capabilities.

## Transient Models

### Authenticated Identity

Produced after successful credential verification and passed to token issuance.

| Field | Rule |
|---|---|
| `UserId` | Stable Identity user ID. |
| `Email` | Current authenticated email. |
| `Roles` | Current assigned role names; Phase 1 built-in administrator includes `Administrator`. |

### Access Token

Not persisted.

| Field | Rule |
|---|---|
| `EncodedToken` | RS256-signed JWT returned once to the caller and never logged. |
| `ExpiresAtUtc` | Absolute UTC expiry returned with the token. |

JWT claims are `sub`, `email`, one `role` per applicable role, `iss`, `aud`, `iat`, `exp`, and
`jti`. The default difference between `exp` and `iat` is 900 seconds and is configurable.

### Initialization State

Runtime-only lifecycle state:

```text
NotStarted → Migrating → Bootstrapping → Ready
                  └──────────────┬──────→ Failed
                                 └───────→ Failed
```

The service accepts normal traffic only in `Ready`. Configuration, RSA, migration, or bootstrap
failure terminates startup and never presents a ready service. After successful startup,
readiness becomes unhealthy if SQLite is no longer accessible.

## External Operational State

### RSA Key Pair

| Artifact | Rule |
|---|---|
| Private PEM | Externally supplied; readable only by Authentication API and authorized operator; mounted read-only where supported; never stored in SQLite, source, image, response, or log. |
| Public PEM | Paired verification material used by tests in Phase 1 and consumer APIs in later phases; contains no signing capability. |

The key directory is outside the Compose project lifecycle. Phase 1 has one active pair and no
key entity, key identifier registry, JWKS document, rotation history, KMS, Vault, or HSM.

## Bootstrap Invariants

1. Migrations complete before bootstrap begins.
2. The built-in user and role are identified by fixed reserved IDs rather than mutable names.
3. If the built-in user is absent, bootstrap ensures the fixed role, creates the fixed user via
   Identity using `admin@local.invalid` / `admin`, and assigns the role in one transaction.
4. If the built-in user exists, bootstrap does not modify email, password, role assignments,
   stamps, lockout state, or other fields.
5. Failure rolls back bootstrap and prevents readiness; retry starts from a consistent state.
6. The Identity password policy is externally configurable and its Phase 1 default permits the
   explicit initial password `admin`; hashing and verification remain Identity-owned.

## Login State Rules

1. Missing, blank, or structurally invalid request data produces `400` ProblemDetails.
2. Email lookup uses Identity normalization and the unique normalized-email constraint.
3. Unknown email performs Identity-hasher-equivalent work but changes no user state.
4. Wrong password for an existing user uses Identity failure accounting.
5. Unknown, wrong, locked, and—when Phase 3 introduces it—disabled states share the same external
   `401` contract.
6. Successful authentication reads current roles and produces a transient access token; it does
   not create a refresh token, cookie, session, or persistent token record.

## Explicitly Absent from Phase 1

- Custom domain User/Role aggregates
- Enabled/disabled administration state
- RefreshToken or SessionFamily
- Password reset/recovery state
- Data Protection key persistence
- Email/outbox records
- JWT revocation/blacklist or key-rotation records
- Audit or device-session entities
