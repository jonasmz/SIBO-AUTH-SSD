# Data Model: Phase 3 — User and Role Administration

This model covers only what Phase 3 adds to the Phase 1 Identity schema. Wire shapes are defined in
[contracts/authentication-api-admin.openapi.yaml](contracts/authentication-api-admin.openapi.yaml).
Design rationale is in [research.md](research.md).

## Persistent Entities (SQLite, Identity schema)

### ApplicationUser (`AspNetUsers`), changed

Derived from `IdentityUser<string>` (Infrastructure only). Identity still owns every existing column.

| Field | Type | Rules |
|---|---|---|
| `Id` | string (GUID text) | Generated at creation; immutable; the built-in administrator keeps its fixed Phase 1 id |
| `UserName` | string | Internal only. New users: equal to `Id`; never exposed and never changed |
| `Email` / `NormalizedEmail` | string | Required; valid syntax; unique after Identity normalization (`EmailIndex`, unique) |
| `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` | string | Identity-managed; **never exposed** |
| `LockoutEnd`, `LockoutEnabled`, `AccessFailedCount` | Identity lockout data | Identity-managed; only `LockoutEnd` is projected, as lockout state |
| **`IsEnabled`** | bool, `INTEGER NOT NULL` | **New.** Defaults to `true` when omitted at creation. Migration default `1` for existing rows. No model-level default |

**Migration**: one new migration adds `IsEnabled`. It changes no other column, index, or data, and
startup applies it automatically (Constitution VII).

### Role (`AspNetRoles`), unchanged schema

| Field | Rules |
|---|---|
| `Id` | Generated for new roles; `Administrator` keeps fixed id `7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f01` |
| `Name` / `NormalizedName` | Required, trimmed, 1–256 chars; unique after normalization (`RoleNameIndex`) |

### Role assignment (`AspNetUserRoles`), unchanged schema

A many-to-many relation between users and roles. A user may hold zero or more roles. Assignments
follow renames because they reference the role id.

## Domain Rule

### AdministratorContinuity (`Authentication.Domain/Administration`)

An **enabled administrator** is a user with `IsEnabled = true` who holds the role with the
`Administrator` id. Lockout does not affect this status.

`Permits(isEnabledAdministratorNow, remainsEnabledAdministrator, enabledAdministratorCount)`:

| Target now enabled admin | Target remains enabled admin | Count | Result |
|---|---|---|---|
| no | — | any | permitted |
| yes | yes | any | permitted |
| yes | no | 1 | **refused** |
| yes | no | ≥ 2 | permitted |

Applies to `disable` and `PUT /users/{id}/roles`. Separately, the `Administrator` role itself can
never be renamed or deleted (FR-017).

## Application Contracts

### Users slice (`Authentication.Application/Features/Users`)

- **`UserView`**: `Id`, `Email`, `Enabled`, `IsLockedOut`, `LockoutEndUtc?` (UTC, only when
  locked), `Roles` (role names sorted by normalized name). Projection of an `ApplicationUser`.
- **`CreateUserCommand`**: `Email`, `Password`, `Enabled` (already defaulted to `true`),
  `Roles` (role names; may be empty).
- **`IUserAdministration`**: `ListAsync`, `FindAsync(id)`, `CreateAsync(command)`,
  `UpdateEmailAsync(id, email)`, `SetEnabledAsync(id, enabled)`, `ReplaceRolesAsync(id, roles)`.
  Every method returns `AdministrationResult<UserView>` or a list of views.

### Roles slice (`Authentication.Application/Features/Roles`)

- **`RoleView`**: `Id`, `Name`.
- **`IRoleAdministration`**: `ListAsync`, `CreateAsync(name)`, `RenameAsync(id, name)`,
  `DeleteAsync(id)`. Every method returns `AdministrationResult<RoleView>`. Delete returns the
  removed role, and the API answers `204`.

### Shared result (`Authentication.Application/Features/Administration`)

- **`AdministrationResult<T>`**: `Value` on success, otherwise `Error` plus a fixed, safe `Detail`.
- **`AdministrationError`**: `Invalid` (→ 400), `NotFound` (→ 404), `Conflict` (→ 409).

## State Transitions

### User enabled state

```text
            create(enabled=true|omitted)          create(enabled=false)
                     │                                     │
                     ▼          disable [continuity ok]    ▼
                ┌─────────┐ ─────────────────────────▶ ┌──────────┐
   enable (noop)│ Enabled │                            │ Disabled │ disable (noop)
                └─────────┘ ◀───────────────────────── └──────────┘
                                     enable
```

- Disabled → login refused with the generic `401`. The password, email, and roles are preserved.
- Enabled → login allowed, subject to Identity lockout, which is independent of this state.
- `disable` on the last enabled administrator → `409`, no transition.
- Already issued access tokens are never affected (FR-019).

### Role lifecycle

```text
create ─▶ Exists ─ rename [name unused, not Administrator] ─▶ Exists
            │
            └─ delete [no assignments, not Administrator] ─▶ (removed)
```

## Validation Summary

| Rule | Owner | Outcome |
|---|---|---|
| JSON shape, content type, unknown members, email syntax, non-blank names ≤ 256 | Api request types | 400 |
| Email unique after normalization | Identity `UserValidator` + `EmailIndex` | 409 |
| Password policy | Identity `PasswordValidator` (configured `Identity:*`) | 400 |
| Role name unique after normalization | Identity `RoleValidator` + `RoleNameIndex` | 409 |
| Referenced roles exist | Infrastructure adapter | 400, nothing applied |
| Administrator continuity | Domain `AdministratorContinuity`, evaluated in the transaction | 409 |
| Administrator role immutable; assigned role not deletable | Infrastructure adapter | 409 |

All mutations run in one SQLite `BEGIN IMMEDIATE` transaction and are fully applied or not at all.
