# Implementation Plan: Phase 3 — User and Role Administration

**Branch**: `003-phase-3-user-role-administration` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-phase-3-user-role-administration/spec.md`

**Active roadmap phase / gate**: Roadmap Phase 3, Gate G3 (Roadmap §9)

## Summary

Add protected user and role administration to Authentication API. The work has four parts:

- **Admin endpoints.** Eleven `/api/admin/*` Minimal API endpoints in one route group. The group
  requires the `Administrator` policy, enforced by JwtBearer validation with the same parameters
  as the Phase 2 consumers. The validation key is the public half of the in-process signing key,
  with a new required `Jwt:ClockSkewSeconds` setting.
- **Enabled state.** A derived `ApplicationUser` with `IsEnabled`, added by one migration. Login
  refuses disabled accounts through the existing generic `401`, after the normal password work.
- **Continuity rule.** A pure Domain rule keeps at least one enabled administrator. It is evaluated
  inside a SQLite `BEGIN IMMEDIATE` transaction that wraps every mutation, so concurrent requests
  cannot jointly break it and failures leave no partial state.
- **Identity does the rest.** Email uniqueness, password policy, role storage, and name
  normalization stay with Identity.

The login contract, token claims, and consumer behavior are unchanged.

## Technical Context

**Language/Version**: .NET 10, target framework `net10.0`, stable C# 14

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs, built-in authorization and ProblemDetails;
ASP.NET Core Identity 10.0.12 with EF Core SQLite 10.0.12 (existing);
`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12, already pinned centrally and newly
referenced by Authentication.Infrastructure (Technical Constraints §33); Microsoft IdentityModel
(existing). No new package version.

**Storage**: Existing single SQLite file owned by Authentication API. One migration adds
`AspNetUsers.IsEnabled` (existing rows → enabled). No new table or index.

**Testing**: xUnit.net v3 on Microsoft Testing Platform. `WebApplicationFactory` with temporary
SQLite files, real Identity, real JwtBearer, and real login-issued tokens. `TestTokenMinter`
signs with the test key for negative cases. A controlled `TimeProvider` checks lockout state. No
sleeps, mocks, or new test project. `tests/acceptance/phase-3.sh` runs on disposable Compose
storage.

**Target Platform**: Linux containers from official .NET 10 images, non-root. Docker Compose with
`auth-api`, `api-a`, and `api-b`, unchanged topology.

**Project Type**: Internal REST web service (Authentication API) plus the unchanged reference
consumer.

**Performance Goals**: No invented targets. Validation is local and in-process. Lists are
unpaged because the installation is small (spec assumption).

**Constraints**: Anti-enumeration parity for disabled accounts (FR-009, NFR-SEC-ENUM-001/004).
No partial writes (FR-004/FR-015). The continuity invariant must hold under concurrency (FR-018).
No Identity, EF, or SQLite internals in responses (FR-021). No secrets in logs (NFR-002). Phase 1
and Phase 2 contracts unchanged (FR-020).

**Scale/Scope**: Eleven endpoints, exactly Roadmap §9.2. `revoke-sessions` is excluded (Phase 4).
One new configuration setting and one migration.

**Roadmap boundaries**: Refresh tokens, sessions, revocation, logout, password change/reset, email,
OpenAPI/Scalar runtime, structured admin event logging, proxy, and frontend remain in later phases
or are excluded. They get no placeholder types or tables.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Pre-research | Post-design | Evidence |
|---|---|---|---|
| I. Baseline precedence and traceability | PASS | PASS | Every decision in [research.md](research.md) cites the SRS (FR-USER-001–008/012/015/016, FR-ROLE-004–015, FR-AUTHZ-001–005, FR-LOGIN-007/012–015, NFR-SEC-ENUM-*, NFR-ERR-*, §34.1, §35.2–35.3, §40), Technical Constraints (§5.3–5.7, §6.3, §7, §17, §18, §27, §33), or the Roadmap (§9, G3). Implementation choices (role references by name, response shapes, error texts) are recorded as choices, not requirements. No contradiction found. |
| II. Incremental vertical capabilities | PASS | PASS | Only the Roadmap §9.2 surface is designed. `IsEnabled` is used by login and disable now. No refresh, session, revocation, or reset types, tables, or endpoints. Tests use only Phase 1–3 behavior. |
| III. Hexagonal boundaries and slices | PASS | PASS | Domain gets a pure continuity rule with no technology references. Application gets per-slice ports and records (Users, Roles) plus one shared result. `ApplicationUser` and the Identity adapters stay in Infrastructure. Endpoints live in `Api/Features/Users` and `Api/Features/Roles`, and one `Administration` group mounts them. `Program.cs` only adds authentication/authorization middleware and one mount call. |
| IV. Simplicity and dependency control | PASS | PASS | No new package version. No handler classes that would only forward to ports. Views are serialized directly. One request-reading helper is shared by both slices. No unit-of-work port or generic repository. No in-process lock, because the SQLite write lock suffices. |
| V. Security by construction | PASS | PASS | Identity performs hashing, verification, lockout, policy, and role storage. IdentityModel/JwtBearer validates RS256 with no authority or metadata and with explicit tolerance. The private key never leaves Authentication API. Disabled-login parity keeps the same hashing work. Responses show only permitted fields. No token error details. Expiry-based residual validity is kept. |
| VI. Tests of implemented behavior | PASS | PASS | Domain rule unit tests. Integration tests over real SQLite, Identity, JwtBearer, and login tokens, including a real concurrent case. Compose acceptance includes restart persistence. Phase 1–2 suites and scripts are rerun as regression. |
| VII. Persistence and deployment integrity | PASS | PASS | The migration is applied automatically at startup and is idempotent. Existing data and administrator changes are preserved (FR-ADMIN-BOOT-007). No new service, volume, or manual step. One added environment variable reuses the existing Compose variable. |
| Repository discipline | PASS | PASS | Central versions, nullable, analyzers, file-scoped namespaces, one type per file, and 0 warnings all apply to the new files. |

**Result**: no violation; Complexity Tracking not required.

## Phase 0: Research Decisions

All decisions are consolidated in [research.md](research.md). No `NEEDS CLARIFICATION` remains.

Key outcomes:

- `ApplicationUser.IsEnabled` is added by a migration (default `1` for existing rows) with no
  model-level default.
- Login checks `IsEnabled` after password verification, keeping timing and response parity.
- JwtBearer in Authentication API mirrors the consumer parameters. A shared `RsaSigningKey`
  supplies both signing and validation. `Jwt:ClockSkewSeconds` is required and must be 0–60.
- The `Administrator` policy is applied on the `/api/admin` group. Bearer `OnChallenge` and
  `OnForbidden` write ProblemDetails.
- Domain owns `AdministratorContinuity`. Application owns the `IUserAdministration` and
  `IRoleAdministration` ports and `AdministrationResult<T>`. Infrastructure Identity adapters
  implement the ports. Endpoints call the ports directly.
- Every mutation runs in one `BEGIN IMMEDIATE` transaction, which gives atomicity and serializes
  concurrent writers.
- Unknown JSON members are rejected via `JsonUnmappedMemberHandling.Disallow`. `PATCH` accepts
  only `email`.
- Roles are referenced by name, and duplicates collapse. A new user's internal `UserName` is its id.

## Phase 1: Design and Contracts

### Design Outputs

- [data-model.md](data-model.md) covers the `ApplicationUser` change, the continuity rule, the
  Application contracts, state transitions, and validation ownership.
- [contracts/authentication-api-admin.openapi.yaml](contracts/authentication-api-admin.openapi.yaml)
  defines the eleven admin operations, the bearer scheme, and the `400`/`401`/`403`/`404`/`409`
  problems.
- [quickstart.md](quickstart.md) covers the build, test, and Compose validation for G3.

### Runtime Design

1. **Configuration**: `JwtOptions` gains `ClockSkewSeconds`, validated in
   `DependencyInjection.Validate` as an integer from 0 to 60. A failure names only the setting.
   Compose sets `Jwt__ClockSkewSeconds: "${AUTH_JWT_CLOCK_SKEW_SECONDS:-30}"` on `auth-api`, the
   same variable the consumers use. `.env.example` documents that it is shared.
2. **Keys**: `RsaSigningKey` (singleton) loads the private PEM once and exposes the signing
   credentials and a public-only `RsaSecurityKey`. `JwtAccessTokenIssuer` consumes it. Startup
   resolves the issuer before initialization, as today.
3. **Authentication/authorization**: JwtBearer is the default scheme, with the parameters
   described in research. The `Administrator` policy
   requires role `Administrator`. `OnChallenge` writes a `401` problem with `WWW-Authenticate:
   Bearer`, and `OnForbidden` writes a `403` problem. `Program.cs` adds `UseAuthentication()` and
   `UseAuthorization()` and calls `app.MapAdministrationEndpoints()`.
4. **Persistence**: `AuthenticationDbContext` becomes `IdentityDbContext<ApplicationUser,
   IdentityRole<string>, string>`. The migration `AddUserEnabledState` adds the column, with its
   migration default edited to `true`. `DatabaseInitializer` creates the administrator with
   `IsEnabled = true`.
5. **Login**: `IdentityCredentialValidator` adds the `IsEnabled` check after the successful
   password check and returns `null` when disabled. Nothing else changes.
6. **User administration** (`UserAdministration`):
   - `List` and `Find` project to `UserView`, including lockout computed from `TimeProvider` and
     sorted role names.
   - `Create` opens a transaction, resolves all role names (any missing → `Invalid`), runs
     `CreateAsync(user, password)` (mapping `DuplicateEmail`/`DuplicateUserName` → `Conflict`, and
     `Password*`/`InvalidEmail` → `Invalid`), then `AddToRolesAsync`, then commits.
   - `UpdateEmail` uses `SetEmailAsync`; reusing the user's own current email is a no-op success.
   - `SetEnabled` returns success without a write when the state is unchanged. When disabling, it
     counts enabled administrators and applies `AdministratorContinuity`.
   - `ReplaceRoles` resolves the target set, computes the diff, applies the continuity rule when
     `Administrator` would be removed from an enabled user, then runs `RemoveFromRolesAsync` and
     `AddToRolesAsync`, then commits.
7. **Role administration** (`RoleAdministration`):
   - `List` returns all roles.
   - `Create` uses `RoleManager.CreateAsync` and maps `DuplicateRoleName` → `Conflict`.
   - `Rename` refuses the `Administrator` id (`Conflict`), then calls `SetRoleNameAsync` +
     `UpdateAsync`.
   - `Delete` refuses the `Administrator` id or any role with assignments (`Conflict`), otherwise
     calls `DeleteAsync`.
   - Every mutation runs in a transaction.
8. **HTTP boundary**:
   - `AdministrationRequests.ReadAsync<T>` requires a JSON content type and rejects unknown
     members.
   - `AdministrationResults` translates `AdministrationResult<T>` to `200`/`201` (with
     `Location`)/`204` and to the problem `400`/`404`/`409` with fixed text.
   - A `DbException` maps to `503`, as for login.
   - The `/api/admin` group is mapped only by `MapAdministrationEndpoints`, which calls
     `MapUserAdministrationEndpoints` and `MapRoleAdministrationEndpoints`.

### Verification Design

Integration scenario classes (new), all using `AuthenticationApiFactory` with a temporary SQLite
file:

1. **`AdministrativeAccessTests`**: each of the eleven operations returns `401` without a token.
   One representative endpoint is called with forged, expired-beyond-tolerance, wrong-issuer,
   wrong-audience, and RS512 tokens → `401` problem with `WWW-Authenticate: Bearer`, and with a
   token expired but within tolerance → accepted. A real login token of a non-admin user → `403`
   problem on each operation. The admin token is authorized on all eleven. Phase 1 login request
   and response shapes are asserted unchanged.
2. **`UserAdministrationTests`**:
   - Create with default and explicit `enabled` and with roles, and the `Location` header.
   - Duplicate email that differs only by case → `409`. Weak password → `400`, with no user
     created (list count unchanged).
   - List and get expose exactly the permitted members.
   - `PATCH` email → login with the new email works and the old email fails. `PATCH` with
     `enabled` or `roles` → `400`. `PATCH` to another user's email → `409`. `PATCH` to the user's
     own current email → `200`. Unknown id → `404`.
   - Disable → login `401`, with body and status equal to a wrong-password login. Idempotent
     disable. Enable → login `200` with the same password. The roles and email survive. A user
     created disabled cannot log in.
   - Lockout is shown in the view after repeated failures, under a controlled clock.
3. **`RoleAdministrationTests`**:
   - Create a role. A case-variant duplicate → `409`.
   - `PUT` roles → the next login token has exactly those role claims. An earlier token still
     carries the old roles at `api-a` (Phase 2 consumer, via `ReferenceConsumerFactory`).
   - Omitting a role removes it from that user only. An empty list → no roles. A nonexistent role
     → `400` and roles unchanged.
   - Rename keeps assignments and new tokens carry the new name. A colliding rename → `409`.
   - Delete unassigned → `204`. Delete assigned → `409`. Unknown id → `404`.
4. **`AdministratorContinuityTests`**:
   - With the sole enabled admin, these return `409` with no change: disable self; `PUT` roles
     without `Administrator`; rename or delete the `Administrator` role.
   - A second, disabled admin does not count (`409`).
   - With two enabled admins, one can be disabled or demoted (`200`), and the remaining one cannot
     be.
   - A locked-out admin still counts as enabled.
   - Concurrency: with exactly two enabled admins, two simultaneous disable requests, one per
     admin, give exactly one `200` and one `409`, and one enabled admin remains.

Unit tests: an `AdministratorContinuity` truth table, and `Jwt:ClockSkewSeconds` accepted 0–60
and rejected otherwise.

Regression: the existing `BootstrapAndHealthTests`, `BootstrapLifecycleTests`, `LoginAndJwtTests`,
and `ConsumerValidationTests` run unchanged. Only test infrastructure changes:
`AuthenticationApiFactory` sets `Jwt__ClockSkewSeconds`, and `TestTokenMinter` gains a constructor
that accepts an existing private PEM. The migration-on-existing-database case is covered by
`BootstrapLifecycleTests`, which restarts on the same file.

Acceptance: `tests/acceptance/phase-3.sh` as described in [quickstart.md](quickstart.md), ending
with `phase-2.sh` (→ `phase-1.sh`).

## Project Structure

### Documentation (this feature)

```text
specs/003-phase-3-user-role-administration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── authentication-api-admin.openapi.yaml
├── checklists/
│   └── requirements.md
└── tasks.md                 # created by /speckit-tasks
```

### Source Code (repository root)

```text
compose.yml                                   # auth-api: + Jwt__ClockSkewSeconds
.env.example                                  # note: AUTH_JWT_CLOCK_SKEW_SECONDS now shared with auth-api
docs/
└── phase-3-operations.md                     # new: admin usage, configuration, G3 evidence
src/
├── Authentication.Domain/
│   └── Administration/
│       └── AdministratorContinuity.cs
├── Authentication.Application/
│   └── Features/
│       ├── Administration/
│       │   ├── AdministrationError.cs
│       │   └── AdministrationResult.cs
│       ├── Users/
│       │   ├── CreateUserCommand.cs
│       │   ├── IUserAdministration.cs
│       │   └── UserView.cs
│       └── Roles/
│           ├── IRoleAdministration.cs
│           └── RoleView.cs
├── Authentication.Infrastructure/
│   ├── Authentication.Infrastructure.csproj  # + JwtBearer PackageReference
│   ├── DependencyInjection.cs                # + clock skew, JwtBearer, policy, admin adapters
│   ├── Identity/
│   │   ├── ApplicationUser.cs                # new
│   │   ├── IdentityCredentialValidator.cs    # + IsEnabled check
│   │   ├── RoleAdministration.cs             # new
│   │   └── UserAdministration.cs             # new
│   ├── Persistence/
│   │   ├── AuthenticationDbContext.cs        # ApplicationUser
│   │   ├── DatabaseInitializer.cs            # ApplicationUser, IsEnabled = true
│   │   └── Migrations/
│   │       └── <timestamp>_AddUserEnabledState.cs (+ Designer, snapshot)
│   └── Security/
│       ├── JwtAccessTokenIssuer.cs           # uses RsaSigningKey
│       ├── JwtOptions.cs                     # + ClockSkewSeconds
│       ├── JwtValidationRegistration.cs      # new: bearer parameters + problem events
│       └── RsaSigningKey.cs                  # new
├── Authentication.Api/
│   ├── Program.cs                            # + UseAuthentication/UseAuthorization, MapAdministrationEndpoints
│   ├── appsettings.json                      # + Jwt:ClockSkewSeconds (empty → must be supplied)
│   └── Features/
│       ├── Administration/
│       │   ├── AdministrationEndpoints.cs    # /api/admin group + policy
│       │   ├── AdministrationRequests.cs
│       │   └── AdministrationResults.cs
│       ├── Users/
│       │   ├── UserAdministrationEndpoints.cs
│       │   ├── CreateUserRequest.cs
│       │   ├── UpdateUserRequest.cs
│       │   └── ReplaceUserRolesRequest.cs
│       └── Roles/
│           ├── RoleAdministrationEndpoints.cs
│           └── RoleNameRequest.cs
└── ReferenceConsumer.Api/                    # unchanged
tests/
├── Authentication.UnitTests/
│   ├── Domain/AdministratorContinuityTests.cs
│   └── Infrastructure/JwtOptionsTests.cs     # + clock skew bounds (via configuration validation)
├── Authentication.IntegrationTests/
│   ├── Infrastructure/
│   │   ├── AuthenticationApiFactory.cs       # + Jwt__ClockSkewSeconds
│   │   └── TestTokenMinter.cs                # + ctor from existing private PEM
│   └── Scenarios/
│       ├── AdministrativeAccessTests.cs
│       ├── UserAdministrationTests.cs
│       ├── RoleAdministrationTests.cs
│       └── AdministratorContinuityTests.cs
└── acceptance/
    └── phase-3.sh
```

**Structure Decision**: The four baseline Authentication projects and the two baseline test
projects are kept. Phase 3 adds Users and Roles slices across the layers, as in Technical
Constraints §5.5, plus a small shared `Administration` folder for the result type and the
protected route group. Both slices use these. `ReferenceConsumer.Api` is untouched and serves only
as regression and as the consumer in the role-claim test.

## Complexity Tracking

No constitutional violation requires justification. Items that could look like extra structure
are justified by current consumers:

| Item | Current consumers | Rationale |
|------|-------------------|-----------|
| `ApplicationUser` | Login, disable/enable, user views | Explicitly permitted by Technical Constraints §7 for the enabled state |
| Shared `Features/Administration` (result + group) | Users and Roles slices | Avoids duplicating identical result and HTTP-translation types; the single group guarantees FR-001 on every endpoint |
| `RsaSigningKey` | Token issuer and JwtBearer validation | One key load serves both signing and validation, instead of reading the PEM twice |
