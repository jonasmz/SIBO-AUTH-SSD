# Data Model: Refresh Tokens, Renewable Sessions and Logout

## Conventions

- All IDs are application-generated GUID strings, matching existing Identity user IDs.
- All timestamps are non-null UTC `DateTimeOffset` values unless explicitly optional.
- The raw browser credential never enters the model or database. `TokenHash` is the SHA-256 digest
  of the decoded opaque credential bytes.
- Revocation and consumption timestamps are write-once. No operation clears them.
- The family expiry is absolute. Every credential in a family has the same `ExpiresAtUtc`.

## Entity: RenewableSessionFamily

One row represents the renewable session established by one successful login.

| Field | Type | Rules |
|---|---|---|
| `Id` | string GUID | Primary key; generated at login; immutable. |
| `UserId` | string | Required FK to `AspNetUsers.Id`; immutable. |
| `CreatedAtUtc` | DateTimeOffset | Required UTC; set from `TimeProvider` at login. |
| `ExpiresAtUtc` | DateTimeOffset | Required UTC; `CreatedAtUtc + configured lifetime`; immutable. |
| `RevokedAtUtc` | DateTimeOffset? | Null while not explicitly revoked; set once. |
| `RevocationReason` | enum/string? | Null with `RevokedAtUtc`; otherwise `Replay`, `Logout`, `UserDisabled`, or `Administrator`. Never exposed over HTTP. |

### Relationships and indexes

- `AspNetUsers (1) -> (many) RenewableSessionFamily`, restrictive delete behavior; routine user
  deletion is out of scope.
- `RenewableSessionFamily (1) -> (many) RefreshCredential`.
- Index `(UserId, RevokedAtUtc)` supports all-active-family revocation.

### Derived state

- **Active**: `RevokedAtUtc is null && now < ExpiresAtUtc`.
- **Expired**: `now >= ExpiresAtUtc`; expiry does not need a cleanup write.
- **Revoked**: `RevokedAtUtc is not null`; irreversible even if the user is enabled later.

## Entity: RefreshCredential

One row represents one link in a family's rotation chain.

| Field | Type | Rules |
|---|---|---|
| `Id` | string GUID | Primary key; generated when issued; immutable. |
| `FamilyId` | string GUID | Required FK to `RenewableSessionFamily.Id`; immutable. |
| `TokenHash` | byte[32] | Required SHA-256 digest; unique; never returned or logged. |
| `CreatedAtUtc` | DateTimeOffset | Required UTC; set from `TimeProvider`. |
| `ExpiresAtUtc` | DateTimeOffset | Required UTC; exactly equals the owning family's absolute expiry. |
| `ConsumedAtUtc` | DateTimeOffset? | Set once by successful rotation. Null before use. |
| `RevokedAtUtc` | DateTimeOffset? | Optional credential-level invalidation marker; write-once. Family revocation remains authoritative. |
| `ReplacedByTokenId` | string GUID? | Unique optional self-FK, assigned with `ConsumedAtUtc`; points to the one replacement. |

### Relationships and indexes

- Unique index on `TokenHash` supports lookup and prevents verifier collision insertion.
- Index on `FamilyId` supports chain inspection and referential operations.
- Optional self-reference `ReplacedByTokenId` uses restrictive delete behavior.
- Check constraints enforce 32-byte hash length, expiry after creation, replacement only with a
  consumption timestamp, and non-null family revocation reason iff `RevokedAtUtc` is non-null.

### Derived state

- **Current**: owning family active, `ConsumedAtUtc is null`, `RevokedAtUtc is null`, and current
  time is before `ExpiresAtUtc`.
- **Consumed**: `ConsumedAtUtc is not null`; presentation is replay, regardless of replacement state.
- **Unusable**: malformed/unknown, expired, revoked, consumed, family inactive, user disabled or
  Identity-locked. All map to the same external refresh failure.

## Existing Entity Extension: ApplicationUser

No new user field is required. The existing `IsEnabled`, `LockoutEnabled` and `LockoutEnd` state is
read during refresh. Its relationship gains zero or more renewable-session families.

- Disabling a user sets `IsEnabled=false` and revokes all active families in the same transaction.
- Enabling sets only `IsEnabled=true`; it never changes a family or credential.
- A refused last-enabled-Administrator disable changes neither the user nor session rows.

## State Transitions

### Successful login

```text
no family
  -> create active family (absolute expiry T)
  -> create current credential (expiry T)
  -> return access-token body + raw credential cookie
```

Failed credentials, disabled state or lockout create neither row.

### Successful rotation

```text
current credential A
  -> set A.ConsumedAtUtc
  -> create current credential B in same family, same expiry
  -> set A.ReplacedByTokenId = B.Id
  -> commit
  -> return new access-token body + B cookie
```

The transaction commits before either credential is returned.

### Replay

```text
consumed credential A presented
  -> set family.RevokedAtUtc + reason Replay (if not already revoked)
  -> commit
  -> generic 401; issue nothing
```

Credential B and any later chain member become unusable through family state.

### Logout

```text
any cookie state
  -> if digest identifies a family, revoke it with reason Logout
  -> clear cookie
  -> 204
```

Repeated, absent, malformed, unknown, expired or already-revoked credentials remain successful.

### Administrative or disablement revocation

```text
all active families for UserId
  -> set one RevokedAtUtc and reason on every matched family
  -> commit
```

Disablement performs this in the same transaction as the accepted user-state change.

## Transaction Invariants

1. At most one transaction changes a credential from current to consumed.
2. A consumed credential has at most one replacement, in the same family.
3. A replacement never extends the family absolute expiry.
4. Once family revocation commits, no refresh transaction can commit a new replacement for it.
5. No access-token state or blacklist is stored; already-issued JWTs retain normal validity.
