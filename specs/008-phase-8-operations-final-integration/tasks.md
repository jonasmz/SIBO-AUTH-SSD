---

description: "Task list for Phase 8 operations, deployment integration and final acceptance"
---

# Tasks: Phase 8 — Operations, Deployment Integration and Final Acceptance

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md),
[deployment-topology.md](contracts/deployment-topology.md),
[operational-log.md](contracts/operational-log.md), and
[openapi-coverage.md](contracts/openapi-coverage.md)

**Prerequisites**: Approved Phase 8 plan and clarified specification; Gate G7 closed (recorded in the
roadmap).

**Tests**: Required, kept to what closes a verification gap. Use the existing xUnit v3 projects,
`WebApplicationFactory`, real Identity and SQLite, `CapturingLoggerProvider`, and the Compose
acceptance scripts; no sleeps in automated tests, no EF InMemory, no new test project or package.
Acceptance runs only on disposable paths created for the run and never touches real production
storage.

**Organization**: Tasks are grouped by user story. US1 delivers persistent logging and the OpenAPI
contract inside the application; US2 delivers the four-service deployment with its frontend/reverse
proxy and adapts the acceptance harness; US3 proves persistence and recovery; US4 composes the
end-to-end acceptance and chains the Phase 7→1 regression. `tests/acceptance/phase-8.sh` is created
in US2 and extended by US3 and US4.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites because it affects a distinct file.
- **[US#]**: The user story served by a task. Shared setup and foundation work has no story label.

## Phase 1: Setup

**Purpose**: Introduce the two baseline-authorized packages and the test support the stories need,
without changing behavior.

- [X] T001 Add `Microsoft.AspNetCore.OpenApi` `10.0.12` and `Scalar.AspNetCore` `2.17.9` to `Directory.Packages.props` and reference both only from `src/Authentication.Api/Authentication.Api.csproj`; pin the transitive `Microsoft.OpenApi` centrally at the version the restore resolves (central transitive pinning is enabled); no other package is added.
- [X] T002 [P] Add empty `Logging:File:Directory` and `Logging:File:RetentionDays` placeholders to `src/Authentication.Api/appsettings.json`, leaving existing `Logging:LogLevel` entries untouched.
- [X] T003 [P] Add test support: `tests/Authentication.UnitTests/Infrastructure/MutableTimeProvider.cs` (a settable `TimeProvider` for rotation and retention) and, in `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs`, a temporary log directory supplied as `Logging__File__Directory` per resource set (overridable), reusing the existing `environment` option.

---

## Phase 2: Foundational Configuration

**Purpose**: Define and validate the log settings that US1 consumes and US2 passes through Compose.

**⚠️ CRITICAL**: Complete this phase before the user stories.

- [X] T004 Add `src/Authentication.Infrastructure/Logging/PersistentFileLoggerOptions.cs` for `Logging:File:Directory` (required) and `Logging:File:RetentionDays` (default `30`, integer ≥ 1), and validate them at startup in `src/Authentication.Infrastructure/DependencyInjection.cs` with the existing `DataProtectionStorageOptions.IsUsableDirectory` probe: a missing, nonexistent, or unwritable directory or an invalid retention terminates startup naming only the setting and never echoing its value.

**Checkpoint**: The new settings exist, are validated, and nothing else changes yet.

---

## Phase 3: User Story 1 - Operational Visibility and API Documentation (Priority: P1) 🎯 MVP

**Goal**: Every security event listed in the roadmap leaves a structured, UTC-stamped, correlated
entry on the console and in a persistent daily file without secrets, and the Authentication API
publishes an accurate OpenAPI contract with a read-only viewer in Development only.

**Independent Test**: Exercise the operations in a Development host and read the console and the log
file; request the contract and the viewer in Development and in Production.

### Tests for User Story 1

- [X] T005 [P] [US1] Add `tests/Authentication.UnitTests/Infrastructure/PersistentFileLoggerProviderTests.cs` using `MutableTimeProvider` and temporary directories: one line per event with UTC time, level, category, event id when non-zero, and trace/span when present; message newlines escaped; an exception written as its type and escaped stack trace without its message; the file named `auth-yyyy-MM-dd.log` by the UTC date and switched when the date changes; files older than the retention removed at startup and at rotation while files not matching the pattern are untouched; many concurrent writers yielding whole, uninterleaved, complete lines; and `Dispose` draining the queue.
- [X] T006 [P] [US1] Add `tests/Authentication.IntegrationTests/Scenarios/OperationalEventsTests.cs`: the six new events (`LoginSucceeded`, `UserCreated`, `UserEnabled`, `UserDisabled`, `UserRoleAssigned`, `UserRoleRemoved`) appear with user id, UTC time, and trace identifier and with no email, password, or token; the host writes these and the existing events to `auth-<utc-date>.log` in the configured directory; the file holds no secret; and a missing or unwritable `Logging__File__Directory` or an invalid `Logging__File__RetentionDays` stops startup naming only the setting.
- [X] T007 [P] [US1] Add `tests/Authentication.IntegrationTests/Scenarios/ApiDocumentationTests.cs`: in Development `/openapi/v1.json` is an OpenAPI 3.1 document that contains the operations, methods, success and error statuses, request bodies, and Bearer requirements of [openapi-coverage.md](contracts/openapi-coverage.md) with the `Bearer` HTTP security scheme, and `/scalar` is served; the viewer is read-only (test-request and client hidden, no persistent authentication); in Production both return `404`.

### Implementation for User Story 1

- [X] T008 [US1] Implement the file provider in `src/Authentication.Infrastructure/Logging/PersistentFileLogger.cs` and `src/Authentication.Infrastructure/Logging/PersistentFileLoggerProvider.cs` (`[ProviderAlias("File")]`): `Log` formats one line on the calling thread as `{utc:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level}] {Category}[{EventId}] trace={TraceId} span={SpanId} {Message}` per [operational-log.md](contracts/operational-log.md) (omitting absent parts, escaping message newlines, writing an exception as ` exception={Type}` plus its escaped stack trace and never its message) and enqueues it on an unbounded `Channel<string>`; one background task appends to `auth-YYYY-MM-DD.log` (UTF-8 without BOM, flushing after each drained batch), switches file when the UTC date of an event changes using `TimeProvider`, deletes `auth-*.log` older than the retention at startup and at each rotation, and `Dispose` completes and drains the queue; no Serilog, NLog, or other framework.
- [X] T009 [US1] Register the provider and the console format: add the file provider from T008 in `src/Authentication.Infrastructure/DependencyInjection.cs` (settings from T004, `TimeProvider` from DI) and configure `builder.Logging.AddSimpleConsole` with single-line UTC timestamps in `src/Authentication.Api/Program.cs`, so console and file receive the same events.
- [X] T010 [P] [US1] Add `LoginSucceeded`, `UserCreated`, `UserEnabled`, `UserDisabled`, `UserRoleAssigned`, and `UserRoleRemoved` source-generated `Information` events to `src/Authentication.Infrastructure/Identity/SecurityEvents.cs` in the existing message style with UTC time and trace/span identifiers and only user id, role count, or role name as data.
- [X] T011 [US1] Emit `LoginSucceeded` from `src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs` once credentials are accepted for an enabled, unlocked account, without changing its control flow or result.
- [X] T012 [US1] Emit `UserCreated` (after commit), `UserEnabled`/`UserDisabled` (only when the enabled state actually changes), and one `UserRoleAssigned`/`UserRoleRemoved` per role of the added and removed sets of a committed replacement from `src/Authentication.Infrastructure/Identity/UserAdministration.cs`, without changing any result.
- [X] T013 [P] [US1] Add `src/Authentication.Api/Documentation/BearerSecuritySchemeTransformer.cs` (document transformer registering the `Bearer` HTTP scheme with `bearerFormat` `JWT`, and an operation transformer adding the requirement to endpoints with authorization metadata and no anonymous metadata) and `src/Authentication.Api/Documentation/ApiDocumentationRegistration.cs` (`AddOpenApi` for document `v1`; a Development-only mapping of `MapOpenApi` and `MapScalarApiReference` with `HideTestRequestButton`, `HideClientButton`, no persistent authentication, no preset credentials, and telemetry off).
- [X] T014 [US1] Register the documentation services and call the Development-only mapping from `src/Authentication.Api/Program.cs`; Production maps neither route.
- [X] T015 [P] [US1] Add endpoint metadata only (summary, `Produces<T>`/`ProducesProblem` for every status the endpoint already returns, `Accepts<T>("application/json")` where the body is read manually) to the anonymous and self-service endpoints: `src/Authentication.Api/Features/Login/LoginEndpoint.cs`, `src/Authentication.Api/Features/Sessions/RefreshEndpoint.cs`, `src/Authentication.Api/Features/Sessions/LogoutEndpoint.cs`, `src/Authentication.Api/Features/Passwords/ChangePasswordEndpoint.cs`, `src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs`, and `src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs`, changing no behavior.
- [X] T016 [P] [US1] Add the same metadata to the administrative endpoints in `src/Authentication.Api/Features/Users/UserAdministrationEndpoints.cs` and `src/Authentication.Api/Features/Roles/RoleAdministrationEndpoints.cs`, taking each operation's exact status set from its current results (a listed code that is not actually produced is dropped from the metadata, never added to behavior).
- [X] T017 [P] [US1] Add summaries and the `200`/`503` responses to the two health operations in `src/Authentication.Api/Features/Health/HealthEndpoints.cs`.

**Checkpoint**: US1 is demonstrable on a single host: persistent correlated logs and the Development-only documentation, with no deployment change.

---

## Phase 4: User Story 2 - Unified and Secure Deployment (Priority: P1)

**Goal**: One `docker compose up` brings up exactly four permanent services behind a single HTTPS
entry point with the public `/auth`, `/api-a`, and `/api-b` URLs, unreachable backends, and the
private key available only to Authentication API; the Phase 1–7 acceptance harness keeps working.

**Independent Test**: Start the production-equivalent stack from empty disposable storage and check
the services, the URL translation table, the cookie path, backend isolation, and key isolation.

### Implementation for User Story 2

- [X] T018 [US2] Add `deploy/frontend/nginx.conf` per [deployment-topology.md](contracts/deployment-topology.md): HTTPS on 443 with the mounted `tls.crt`/`tls.key` and a `301` from port 80; static files from `/usr/share/nginx/html` with SPA fallback; `/auth/admin/<resource>` → `http://auth-api:8080/api/admin/<resource>` with `proxy_redirect /api/admin/ /auth/admin/`; `/auth/<operation>` → `http://auth-api:8080/api/auth/<operation>`, so redundant or internal forms (`/auth/api/auth/login`, `/auth/health/ready`, `/auth/openapi/v1.json`, `/auth/scalar`) reach non-existent routes and return `404`; `proxy_cookie_path /api/auth /auth`; `/api-a/api/*` and `/api-b/api/*` with the prefix stripped and any other path under them `404`; the Phase 7 first limiting layer (`limit_req_zone $binary_remote_addr` 60 r/min, burst 20, `limit_req_status 429`) on login, refresh, forgot-password, and reset-password; `Host`, `X-Forwarded-For $remote_addr`, and `X-Forwarded-Proto $scheme` overwritten on every proxied request; `client_max_body_size 16k`.
- [X] T019 [US2] Rewrite `compose.yml` as the production topology: exactly `frontend` (official `nginx` stable Alpine image pinned by the digest validated in Phase 7; read-only mounts of `deploy/frontend/nginx.conf`, `${FRONTEND_STATIC_HOST_PATH:?}`, and `${FRONTEND_TLS_HOST_PATH:?}`; the only published ports `${FRONTEND_HTTPS_PORT:-443}` and `${FRONTEND_HTTP_PORT:-80}`), `auth-api`, `api-a`, and `api-b` with no published ports; `ASPNETCORE_ENVIRONMENT=Production` on the three .NET services; the default network on `${AUTH_INTERNAL_SUBNET:-172.30.80.0/24}` with the frontend at `${FRONTEND_INTERNAL_ADDRESS:-172.30.80.10}` and `ReverseProxy__TrustedProxies` set to `${AUTH_TRUSTED_PROXIES:-172.30.80.10}`; the `auth-api` log mount `${AUTH_LOGS_HOST_PATH:?}` → `/app/logs` with `Logging__File__Directory=/app/logs` and the optional `AUTH_LOG_RETENTION_DAYS` mapped to the retention; the private key mounted read-only only into `auth-api` and a single public-key file into the consumers; no migration, bootstrap, gateway, database, cache, secrets, backup, or observability service.
- [X] T020 [P] [US2] Add a Phase 8 section to `.env.example` documenting the new variables (`FRONTEND_STATIC_HOST_PATH`, `FRONTEND_TLS_HOST_PATH`, `FRONTEND_HTTPS_PORT`, `FRONTEND_HTTP_PORT`, `AUTH_LOGS_HOST_PATH`, `AUTH_LOG_RETENTION_DAYS`, `AUTH_INTERNAL_SUBNET`, `FRONTEND_INTERNAL_ADDRESS`) with the reference paths under `/srv/auth-system/` and the ownership and mode requirements of the topology contract.
- [X] T021 [P] [US2] Add the acceptance-only override `tests/acceptance/compose.direct-access.yml` publishing `auth-api` (`${AUTH_HTTP_PORT}:8080`), `api-a` (`${API_A_HTTP_PORT}:8080`), and `api-b` (`${API_B_HTTP_PORT}:8080`) for the Phase 1–7 procedures that address backends directly; `compose.yml` itself never publishes them.
- [X] T022 [P] [US2] Add the minimal static test page `tests/acceptance/frontend-test-page/index.html`, used only by acceptance to prove file serving and routing and not a product deliverable.
- [X] T023 [US2] Add `tests/acceptance/deployment-env.sh`, sourced by every acceptance script: it creates disposable `AUTH_LOGS_HOST_PATH` (writable by the container user with the same convention the caller uses for its data directory), `FRONTEND_STATIC_HOST_PATH` (the test page), and `FRONTEND_TLS_HOST_PATH` (a disposable self-signed pair readable by Nginx) under the caller's `$STATE`, and exports `COMPOSE_FILE` with `tests/acceptance/compose.direct-access.yml` appended to whatever the caller already set, because Compose validates every `${VAR:?}` even when only `auth-api` is started.
- [X] T024 [US2] Adapt `tests/acceptance/phase-1.sh`, `tests/acceptance/phase-2.sh`, `tests/acceptance/phase-3.sh`, `tests/acceptance/phase-4.sh`, `tests/acceptance/phase-5.sh`, `tests/acceptance/phase-6.sh`, and `tests/acceptance/phase-7.sh` to source T023, with `tests/acceptance/phase-7.sh` additionally exporting `AUTH_INTERNAL_SUBNET=172.28.7.0/24` and a `FRONTEND_INTERNAL_ADDRESS` inside it that differs from its reference proxy's `172.28.7.10` (research §9), leaving every assertion unchanged (Phase 1–7 evidence is reused, not redone), and update `tests/acceptance/compose.reference-proxy.yml` to rely on `compose.yml`'s network variables instead of redefining the network so two network definitions never merge.
- [X] T025 [US2] Create `tests/acceptance/phase-8.sh` with the deployment section against `compose.yml` plus only the Phase 6 mail-sink override and no direct-access override, all traffic through `https://localhost:$FRONTEND_HTTPS_PORT` with `curl --cacert` on the disposable certificate: exactly four services run; the schema and initial administrator exist and readiness is confirmed from inside the network; the test page is served at `/`; each public URL of the contract table reaches its unchanged internal route (a bad login answers the route's own `401` problem details, never the page or a proxy `404`); a created user's `Location` starts with `/auth/admin/users/`; `/auth/api/auth/login`, `/auth/health/ready`, `/auth/openapi/v1.json`, `/auth/scalar`, and `/api-a/health/live` return `404`; backend ports are unreachable from the host; and the consumers' mounts hold no private key while `auth-api` holds it. The script gives the private signing key the restricted ownership and mode of [deployment-topology.md](contracts/deployment-topology.md) (owned by the container UID, file `0600`, directory `0700`, set with a throwaway root container of the official runtime image as Phase 6 does), asserts them before startup and after the run, and shows `auth-api` signs with it while the consumers cannot read it. The other disposable directories (data, key ring, logs) keep the writable convention of the Phase 1–7 scripts so the script, running as an unprivileged operator, can back up, inspect, and adjust them; they are not evidence of production modes, which `docs/phase-8-operations.md` documents (T028). Because the unprivileged script cannot enter the restricted key directory, every later check of the private key's existence (T026) and its removal on exit go through the same throwaway root container.

**Checkpoint**: US2 deploys the unified stack from empty storage and the Phase 1–7 harness still works against it.

---

## Phase 5: User Story 3 - Persistence and Recovery (Priority: P1)

**Goal**: Critical storage survives restart, rebuild, recreation, and `docker compose down -v`, every
persistent path is documented, and a SQLite backup taken under writes restores into a working,
verified instance.

**Independent Test**: Establish identifiable state, run the lifecycle operations and `down -v` in the
disposable environment, then back up under writes, restore into a separate disposable project, and
authenticate a known account.

### Implementation for User Story 3

- [X] T026 [US3] Extend `tests/acceptance/phase-8.sh` with the lifecycle section: after establishing users, a role, a changed administrator password, and an active refresh session, run `docker compose restart`, `up --build`, and `up --force-recreate`, then `down -v` followed by `up -d` on the same disposable host paths, asserting after each that the database, key ring, private key, and log files still exist and that users, roles, the changed password, the session state, and API A and API B validation of a token signed with the persisted key are intact; the script uses only disposable paths and never real storage.
- [X] T027 [US3] Extend `tests/acceptance/phase-8.sh` with the backup and restore section: take a backup with `sqlite3 ".backup"` of the live database while a loop of logins and refreshes writes to it, require `PRAGMA integrity_check` to print `ok`, start a separate disposable Compose project (its own `COMPOSE_PROJECT_NAME`, `AUTH_INTERNAL_SUBNET`, `FRONTEND_INTERNAL_ADDRESS`, `AUTH_TRUSTED_PROXIES`, and published ports, so it never collides with the running project's network or ports) on a fresh data directory holding only the restored file, verify the users, roles, persisted session rows, and a known login, and tear that project down; a copy that merely exists is not accepted.
- [X] T037 [P] [US3] Add `tests/Authentication.IntegrationTests/Scenarios/OlderBackupRestoreTests.cs` for the "backup from an older version" edge case: a temporary SQLite file migrated only up to an earlier migration (through EF Core's migrator) and holding a user stands in for an older backup; starting the host on it applies the pending migrations, keeps the existing user and the administrator's password, does not recreate the database, and reports ready.
- [X] T028 [US3] Create `docs/phase-8-operations.md` with the persistence and recovery guidance: every persistent path with its host variable, container path, ownership, and mode (database, key ring, private and public keys, logs, static files, TLS pair), that `docker compose down -v` removes none of them and deleting data requires removing the host path explicitly, the backup procedure (`sqlite3 .backup`, integrity check, concurrency note), the restore procedure (stop auth-api, move the old database and its journal/WAL side files aside, copy the backup in with the container user's ownership and mode, start, wait for readiness, and the effect of pending migrations), and that the keys and key ring are copied as plain files; deployment, logging, OpenAPI, and Gate G8 evidence sections are completed in T033–T036.

**Checkpoint**: US3 shows state outlives the lifecycle and backups are proven restorable.

---

## Phase 6: User Story 4 - Final System Acceptance (Priority: P2)

**Goal**: One reproducible end-to-end procedure over the public URLs, plus the chained Phase 7→1
regression, accepts the complete MVP on integrated evidence.

**Independent Test**: Run `tests/acceptance/phase-8.sh` from empty disposable storage and review its
recorded output.

### Implementation for User Story 4

- [X] T029 [US4] Extend `tests/acceptance/phase-8.sh` with the end-to-end section using only public URLs (`/auth/*`, `/auth/admin/*`, `/api-a/api/*`, `/api-b/api/*`) and a cookie jar acting as the browser: administrator login and password replacement; user creation and role assignment; user login whose `Set-Cookie` is `auth_refresh` with `Path=/auth`, `HttpOnly`, `Secure`, and `SameSite=Strict`; API A and API B accept the token; `/auth/refresh` rotates the cookie under the same public path and attributes and replay of the old value is refused; a refresh from a foreign `Origin` is refused; `/auth/logout` clears the cookie under `Path=/auth` and the cleared jar can no longer renew; re-login; forgot-password and reset using the mail sink with the previous sessions revoked; user disablement and the rejected login; account lockout and its recovery by moving the persisted lockout end into the past with `sqlite3` (no waiting); an application `429` and the proxy's own first-layer `429`; and the Phase 7 trust behavior through the entry point: requests carrying a client-forged `X-Forwarded-For` through `/auth/login` are rate limited as the real client, and the `RateLimitApplied` event names the client's address as seen by the frontend, never the frontend's internal address (`FRONTEND_INTERNAL_ADDRESS`) nor the forged value.
- [X] T030 [US4] Extend `tests/acceptance/phase-8.sh` with the log review: the file `auth-<utc-date>.log` exists on the host path and contains every event of Roadmap §14.2 (login success and failure, lockout, rate limit, logout, password change, password reset, user create, user enable and disable, role assign and remove, refresh reuse detected, sessions revoked) with UTC time and a trace identifier, and neither the console output nor the file contains a password, access or refresh token, reset token, private key, or SMTP credential.
- [X] T031 [US4] Extend `tests/acceptance/phase-8.sh` to finish by tearing down its own Compose project (`down -v`) and then running `tests/acceptance/phase-7.sh` as regression (which chains Phases 6→1) in a subshell with this script's exported settings (including `COMPOSE_FILE`, `COMPOSE_PROJECT_NAME`, the network variables, and the frontend paths) unset, to print `Phase 8 acceptance: ALL PASS`, and to remove every disposable path and Compose project on exit.

**Checkpoint**: US4 is the single procedure the project owner can rerun to accept the MVP.

---

## Phase 7: Gate G8 Verification and Documentation

**Purpose**: Verify the scope boundaries, run the full evidence, and complete the operator
documentation before the owner's approval.

- [X] T032 Verify the scope boundaries and record the results in `docs/phase-8-operations.md`: `docker compose config --services` lists exactly `frontend`, `auth-api`, `api-a`, and `api-b`; the package diff against `main` adds only `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, and the pinned transitive `Microsoft.OpenApi`; no migration or model change exists (`dotnet ef migrations has-pending-model-changes` reports none, supplying `Microsoft.EntityFrameworkCore.Design` temporarily from the local package cache and reverting that change); and no logging framework, collector, queue, cache, or gateway was added.
- [X] T033 Complete `docs/phase-8-operations.md` with the deployment procedure (required variables and inputs, how to supply the compiled frontend files, TLS, starting with `docker compose up -d`, image digests), the public URL table, the logging guide (settings, line format, rotation and retention, event list, secret-free guarantee), the OpenAPI and viewer exposure rules (Development only; absent and unrouted in Production), and the acceptance instructions; state that the Angular source and its build belong to the frontend project and that Gate G8 approval and the closing commit or tag are controlled by the project owner.
- [X] T034 Run `dotnet build --no-incremental -warnaserror` and the complete unit/integration suite, resolve Phase 8 and Phase 1–7 regressions, and record the exact commands and PASS evidence in `docs/phase-8-operations.md`, keeping `specs/008-phase-8-operations-final-integration/quickstart.md` as the reusable validation guide.
- [X] T035 Run `tests/acceptance/phase-8.sh` and record the five Gate G8 evidence states—build, tests, startup, feature, and regression—together with the image digests validated by that run in `docs/phase-8-operations.md`, linked to the acceptance output.
- [X] T036 After T001-T035, review implementation, logs, the OpenAPI contract, and evidence against Roadmap §14.9, present `docs/phase-8-operations.md` to the project owner, and obtain explicit Gate G8 approval. Only after that approval, update the authorized Phase 8 checklist/status/progress records in `baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md` and `specs/008-phase-8-operations-final-integration/checklists/requirements.md`, record the approval date/evidence reference without changing normative requirements, and create the identifiable Gate G8 closing commit and tag; never mark Phase 8 complete or create the closing commit or tag before approval.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1**: T001 has no dependencies; T002 and T003 run in parallel with it.
- **Phase 2**: T004 depends on T002 and T003: once the log directory is required and validated, every
  integration test needs the factory's temporary directory from T003.
- **US1** depends on Phases 1–2: T005 needs T003; T008 needs T004; T009 needs T008; T011 and T012 need
  T010; T014 needs T013; T015–T017 are independent metadata edits that T007 verifies.
- **US2** depends on Phase 2 (log settings) and on US1 only for the log mount to be meaningful: T019
  needs T018; T023 needs T022 and T021; T024 needs T023 and T019; T025 needs T019, T023, and T018.
- **US3** depends on US2 (it extends `phase-8.sh` and relies on the deployed stack); T028 can start
  once T019 fixes the paths; T037 depends only on Phase 2 and can run in parallel with US2.
- **US4** depends on US2 and US3 (it extends `phase-8.sh` and composes the whole run) and on US1 for
  the log assertions.
- **Gate G8 verification** depends on all story phases.

### User Story Completion Order

```text
Setup → Foundation → US1 → US2 → US3 → US4 → Gate G8
```

### Parallel Opportunities

- T002/T003, T005/T006/T007, T010 with T013, T015/T016/T017, T020/T021/T022, and T037 can run in parallel once
  their prerequisites are satisfied. T025–T027 and T029–T031 all edit `tests/acceptance/phase-8.sh`
  and `docs/phase-8-operations.md` edits (T028, T032–T035) all edit one file, so those run
  sequentially.

## Parallel Example: User Story 1

```text
Task: "Add PersistentFileLoggerProviderTests in tests/Authentication.UnitTests/Infrastructure/PersistentFileLoggerProviderTests.cs"
Task: "Add OperationalEventsTests in tests/Authentication.IntegrationTests/Scenarios/OperationalEventsTests.cs"
Task: "Add ApiDocumentationTests in tests/Authentication.IntegrationTests/Scenarios/ApiDocumentationTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete setup and the log settings.
2. Complete US1 and prove persistent correlated logging and the Development-only contract on one host.
3. Validate US1 independently before changing the deployment.

### Incremental Delivery

1. US1 → persistent logs, missing events, OpenAPI and read-only viewer.
2. US2 → four-service production topology, frontend/reverse proxy, adapted harness.
3. US3 → lifecycle and `down -v` survival, backup and restore.
4. US4 → end-to-end acceptance and chained regression.
5. Gate G8 → boundary checks, documentation, evidence.

## Notes

- Do not add an endpoint, entity, table, migration, permanent service, gateway, logging framework,
  collector, queue, cache, or any frontend product feature; the Angular application is an
  owner-supplied input.
- A defect found in an earlier phase is reported against its originating requirement and corrected in
  that phase, not absorbed here.
- Keep passwords, tokens, reset tokens, private keys, SMTP credentials, and emails of limited or failed
  requests out of logs, contracts, and assertions.
- Do not change `plan.md`, the baseline documents, any closed phase record, or any Phase 1–7 contract;
  acceptance never touches real production storage.

---

## Phase 8: Convergence

- [X] T038 Make the background writer in `src/Authentication.Infrastructure/Logging/PersistentFileLoggerProvider.cs` survive I/O failures instead of dying silently: catch I/O and access errors around opening, writing, flushing, and retention deletion (including `UnauthorizedAccessException`, not only `IOException`) so a transient failure (e.g. disk full) drops or retries the affected batch and the writer resumes on later events, and if the writer still terminates, record that once to the console without the exception message and make `Enqueue` stop queueing so the unbounded channel cannot grow for the process lifetime; add a case to `tests/Authentication.UnitTests/Infrastructure/PersistentFileLoggerProviderTests.cs` (no sleeps) proving logging continues or stops bounded after a write failure per FR-003, FR-004, US1/AC3 (partial)
