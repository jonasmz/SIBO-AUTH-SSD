---

description: "Task list for Phase 7 security hardening"
---

# Tasks: Phase 7 — Security Hardening

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [quickstart.md](quickstart.md),
[authentication-api-rate-limits.openapi.yaml](contracts/authentication-api-rate-limits.openapi.yaml),
and [configuration-and-proxy.md](contracts/configuration-and-proxy.md)

**Prerequisites**: Approved Phase 7 plan and clarified specification; Gate G6 closed (recorded in
the roadmap).

**Tests**: Required. Use the existing xUnit v3 projects, `WebApplicationFactory`, real Identity and
SQLite, and `CapturingLoggerProvider`; no sleeps, EF InMemory, new test projects, new packages, or
unneeded mocks. Scenarios are consolidated per [quickstart.md](quickstart.md); do not add one test
per setting or per requirement. No automated test waits for wall-clock time ([research.md](research.md) §4).

**Organization**: Tasks are grouped by user story. Shared lockout defaults and rate-limit
registration are completed first. US1 builds lockout/limit behavior, US2 adds trusted-proxy handling
and verifies the browser boundary, US3 verifies enumeration resistance and secret protection. Most
cookie, origin, and enumeration behavior already exists from Phases 1–6 and is verified, not rebuilt.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after its stated prerequisites because it affects a distinct file.
- **[US#]**: The user story served by a task. Shared setup and foundation work has no story label.

## Phase 1: Setup and Configuration Baseline

**Purpose**: Make the new optional settings expressible and keep Phase 1–6 regression runs
unaffected, without changing any behavior yet.

- [X] T001 Add the optional Phase 7 settings: empty `RateLimiting` (`Login`, `Refresh`, `ForgotPassword`, `ResetPassword`, `ForgotPasswordAddress`, each with `PermitLimit` and `WindowSeconds`) and `ReverseProxy` (`TrustedProxies`, `TrustedNetworks`) placeholders in `src/Authentication.Api/appsettings.json`; map the optional variables named in [configuration-and-proxy.md](contracts/configuration-and-proxy.md) to the environment of `auth-api` in `compose.yml` — `AUTH_RATE_LIMIT_*` to `RateLimiting__*` and `AUTH_TRUSTED_PROXIES`/`AUTH_TRUSTED_NETWORKS` to `ReverseProxy__*` with empty defaults (blank means the documented default or an empty trusted set), and `AUTH_LOCKOUT_MAX_FAILED_ATTEMPTS`/`AUTH_LOCKOUT_DURATION` to `Identity__Lockout__MaxFailedAccessAttempts`/`Identity__Lockout__DefaultLockoutTimeSpan` with the non-empty defaults `5` and `00:15:00` (Identity's configuration binding is not given blank values); document them in a Phase 7 section of `.env.example`; add no service and no required variable.
- [X] T002 [P] Extend the reusable test host in `tests/Authentication.IntegrationTests/Infrastructure/AuthenticationApiFactory.cs` and add `tests/Authentication.IntegrationTests/Infrastructure/TestConnectionAddressStartupFilter.cs`: a test-only startup filter that runs before every other middleware and sets `Connection.RemoteIpAddress` from the `X-Test-Connection-Address` request header (default `192.0.2.10`), so per-origin and forwarded-header behavior is observable in `TestServer`; and lifted default limits (`10000` permits per policy) unless a test supplies its own `RateLimiting__*` settings, so Phase 1–6 scenarios stay unaffected.
- [X] T003 Keep the Phase 1–6 acceptance regression independent of the new limits by exporting generous `AUTH_RATE_LIMIT_*` values (permit limits large enough for one script run, windows unchanged) in `tests/acceptance/phase-1.sh`, `tests/acceptance/phase-2.sh`, `tests/acceptance/phase-3.sh`, `tests/acceptance/phase-4.sh`, `tests/acceptance/phase-5.sh`, and `tests/acceptance/phase-6.sh`, defaulting each variable like the existing exports and adding them to the regression subshell `unset` lists.

---

## Phase 2: Foundational Lockout and Rate-Limiting Infrastructure

**Purpose**: Establish the verified lockout defaults and the independent per-policy limiter that all
stories use.

**⚠️ CRITICAL**: Complete this phase before implementing any user story.

- [X] T004 Set the initial Identity lockout in the `AddIdentityCore` setup action of `src/Authentication.Infrastructure/DependencyInjection.cs`: `Lockout.MaxFailedAccessAttempts = 5`, `Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)`, `Lockout.AllowedForNewUsers = true`, keeping the existing `Configure<IdentityOptions>(configuration.GetSection("Identity"))` registered afterwards as the external override path, and add startup validation naming only the setting when the effective count is `< 1` or the span is `<= 0`; create no custom counter.
- [X] T005 [P] Add the request-limit settings types in `src/Authentication.Api/Security/`, one top-level type per matching file: `RateLimitPolicy.cs` (`PermitLimit`, `WindowSeconds`, both positive integers) and `RateLimitingOptions.cs` (the five policies with these documented defaults, a project decision overridable per `RateLimiting:<Policy>:PermitLimit` and `:WindowSeconds`: `Login` 10 permits / 60 s, `Refresh` 30 / 60 s, `ForgotPassword` 5 / 900 s, `ResetPassword` 10 / 900 s, `ForgotPasswordAddress` 3 / 3600 s), where blank or missing values keep the default and a non-positive or unparsable value fails startup naming only the setting.
- [X] T006 [P] Add `src/Authentication.Api/Security/TooManyRequests.cs`: the shared `429 Too Many Requests` ProblemDetails writer (title `Too Many Requests`, no account, token, or policy-internal data, `Retry-After` when the limiter reports it) and the `RateLimitApplied` `LoggerMessage` `Warning` event with policy name, effective client address (omitted for the address policy), UTC time from `TimeProvider`, and `Activity` trace/span identifiers, never an email, password, or token.
- [X] T007 Add `src/Authentication.Api/Security/RateLimitingRegistration.cs`: `AddRateLimiter` with four named fixed-window policies `login`, `refresh`, `forgot-password`, `reset-password` (`QueueLimit = 0`), each partitioned by `HttpContext.Connection.RemoteIpAddress` with an IPv4-mapped IPv6 address folded to IPv4 via `MapToIPv4()` and a missing address falling into the single partition `"unknown"`, `RejectionStatusCode = 429`, and `OnRejected` delegating to T006; no package and no custom limiter algorithm.
- [X] T008 Wire the foundation in `src/Authentication.Api/Program.cs`: register the options and rate limiting, and place `app.UseRateLimiter()` after `UseExceptionHandler` and before `UseAuthentication`, changing no other middleware or endpoint mapping.
- [X] T009 [P] Add host-level configuration scenarios in `tests/Authentication.IntegrationTests/Scenarios/SecurityConfigurationTests.cs`: without any setting the effective lockout is `5` / `15 min` and each policy has its documented default; an override of one policy or of the lockout is applied and the others keep their defaults; a non-positive or unparsable limit, lockout count, or lockout span terminates startup naming only the setting and never echoing its value (quickstart `SecurityConfigurationTests`).

**Checkpoint**: Lockout defaults are explicit and overridable, and the limiter is registered but not yet attached to any endpoint.

---

## Phase 3: User Story 1 - Protection Against Brute Force and Request Abuse (Priority: P1) 🎯 MVP

**Goal**: Five failures lock an account for fifteen minutes by default, each of the four anonymous
endpoints has its own per-origin limit answering `429`, recovery is also limited per normalized
address, and lockout and limits never touch each other.

**Independent Test**: Lock an account with five wrong passwords and recover it after expiry;
exceed each endpoint limit from one origin and observe `429` while the others still answer;
verify a limited request never changes lockout state.

### Tests for User Story 1

- [X] T010 [P] [US1] Add lockout scenarios in `tests/Authentication.IntegrationTests/Scenarios/AccountLockoutTests.cs`: five consecutive wrong-password logins lock the account and the correct password is then refused with the same generic `401` as any credential failure; `LockoutEnd` lies within `[before + 15 min, after + 15 min]` bounds around the fifth request; moving the persisted `LockoutEnd` into the past through `UserManager` makes the correct password authenticate again (Identity 10 reads the wall clock, not `TimeProvider`); a successful login resets the counter; and the captured `AccountLockedOut` event carries the user id, `Source` `Login`, UTC time, and trace identifier with no password (quickstart `AccountLockoutTests`).
- [X] T011 [P] [US1] Add rate-limit scenarios in `tests/Authentication.IntegrationTests/Scenarios/RateLimitingTests.cs`: with small explicit `RateLimiting__*` permits and long windows, each of login, refresh, forgot-password, and reset-password returns `429` `application/problem+json` with no account data once exceeded from one connecting address while the other three policies still answer normally and a different address is unaffected; requests within the limit answer exactly as before; the per-address limit returns `429` across varying origins and letter case for an existing and an unknown address alike while a `400` consumes no allowance; a rate-limited login attempt does not increment the account's lockout counter, and a locked account is refused by lockout alone from a fresh address; requests from `192.0.2.10` and `::ffff:192.0.2.10` share one allowance; every `429` carries a `Retry-After` that is positive and no longer than the configured window; and every `429` leaves a `RateLimitApplied` event with policy and address but no email (quickstart `RateLimitingTests`).

### Implementation for User Story 1

- [X] T012 [US1] Add the login security events in `src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs` without changing its control flow or result: a `LoginFailed` `LoggerMessage` `Warning` with `Reason` `UnknownAccount`, `WrongPassword`, `LockedOut`, or `Disabled`, `UserId` only when the account is known, UTC time, and trace/span identifiers (never the submitted email or password), and an `AccountLockedOut` `Warning` with `UserId`, `LockoutEndUtc`, `Source` `Login` emitted when `AccessFailedAsync` leaves the account locked.
- [X] T013 [US1] Emit the same `AccountLockedOut` event with `Source` `ChangePassword` from `src/Authentication.Infrastructure/Identity/PasswordChange.cs` when its existing `AccessFailedAsync` call leaves the account locked, preserving the approved Phase 5 behavior (incorrect current password counts, a policy-rejected new password does not).
- [X] T014 [US1] Attach the policies with `.RequireRateLimiting("login")`, `.RequireRateLimiting("refresh")`, and `.RequireRateLimiting("reset-password")` in `src/Authentication.Api/Features/Login/LoginEndpoint.cs`, `src/Authentication.Api/Features/Sessions/RefreshEndpoint.cs`, and `src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs` respectively, leaving the logout endpoint unlimited and every in-limit response unchanged.
- [X] T015 [US1] Add `src/Authentication.Api/Features/PasswordRecovery/RecoveryAddressLimiter.cs`, a singleton over a `PartitionedRateLimiter<string>` (fixed window, `QueueLimit = 0`, settings `RateLimiting:ForgotPasswordAddress:*`) keyed by `ILookupNormalizer.NormalizeEmail`, disposed with the host, and use it in `src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs` together with `.RequireRateLimiting("forgot-password")`: check the address after the `400` validation and before the readiness check so existing and unknown addresses consume identically, and reject through the shared `429` writer with a `RateLimitApplied` event for policy `forgot-password-address` that logs no address; no external storage.

**Checkpoint**: US1 is independently demonstrable behind a direct connection; lockout and limits are independent and in-limit responses are unchanged.

---

## Phase 4: User Story 2 - Trusted Network and Browser Boundaries (Priority: P1)

**Goal**: The service acts on the real client origin only when the request comes from an explicitly
configured proxy, never from a forged header, and the cookie and origin controls of Phases 1–6 are
confirmed unchanged.

**Independent Test**: Send identical forged `X-Forwarded-For` values from an untrusted and from a
configured peer and compare the effective origin the limiter uses; verify cookie attributes, logout
cleanup, foreign-origin refusal, and the absence of CORS.

### Tests for User Story 2

- [X] T016 [P] [US2] Add forwarded-header scenarios in `tests/Authentication.IntegrationTests/Scenarios/ForwardedHeadersTests.cs` using the T002 connecting-address filter and small explicit limits: with an empty trusted set a forged `X-Forwarded-For` has no effect, so rotating its value neither resets nor shifts a limit; the same header from a configured trusted proxy or network address makes the forwarded client the partition; entries before the first trusted hop in a chain are ignored; running `ForwardedHeadersMiddleware` with the host's resolved `ForwardedHeadersOptions` over a `DefaultHttpContext` shows that `X-Forwarded-Proto` sets the effective scheme only from a trusted peer and that `X-Forwarded-Host` never changes the host; and an unparsable `ReverseProxy__*` entry terminates startup naming only the setting (quickstart `ForwardedHeadersTests`).
- [X] T017 [P] [US2] Add browser-boundary scenarios in `tests/Authentication.IntegrationTests/Scenarios/BrowserBoundaryTests.cs`: with the host environment set to `Production`, login and refresh set `auth_refresh` with `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`, and no `Domain`; logout clears it with the same attributes and the old value no longer refreshes; refresh and logout from a foreign, missing, malformed, or duplicated `Origin` are refused with `403` before the cookie is processed while the configured origin succeeds; and a preflight `OPTIONS` from a foreign origin receives no `Access-Control-Allow-*` header (quickstart `BrowserBoundaryTests`).

### Implementation for User Story 2

- [X] T018 [P] [US2] Add `src/Authentication.Api/Security/ReverseProxyOptions.cs` for `ReverseProxy:TrustedProxies` (comma-separated IP addresses) and `ReverseProxy:TrustedNetworks` (comma-separated CIDR networks), both empty by default, where an unparsable entry fails startup naming only the setting.
- [X] T019 [US2] Add `src/Authentication.Api/Security/ForwardedHeadersRegistration.cs`: configure `ForwardedHeadersOptions` with `ForwardedHeaders = XForwardedFor | XForwardedProto`, clear `KnownProxies` and `KnownIPNetworks` (the framework pre-trusts loopback) and refill them only from T018, `ForwardLimit = null`, `RequireHeaderSymmetry = false`, never `X-Forwarded-Host` and never the `KnownNetworks` obsolete member; with both lists empty no forwarded header is honored.
- [X] T020 [US2] Register the forwarded-headers configuration and call `app.UseForwardedHeaders()` as the first middleware, before `UseExceptionHandler` and `UseRateLimiter`, in `src/Authentication.Api/Program.cs`, so the effective address is the one the limiter and every log use.
- [X] T021 [US2] Confirm the cookie, origin, and CORS controls need no change by running T017; correct `src/Authentication.Api/Features/Sessions/RefreshCookieWriter.cs`, `src/Authentication.Api/Features/Sessions/BrowserOriginValidator.cs`, or `src/Authentication.Api/Program.cs` only if a scenario exposes a divergence from the requirement, leaving cookie name, rotation, and replay behavior untouched and enabling no CORS; the expected result is no production change.

**Checkpoint**: US2 shows the effective origin cannot be forged and the browser boundary holds; limits now key on the trustworthy address.

---

## Phase 5: User Story 3 - Confidential Authentication Responses (Priority: P2)

**Goal**: Login, recovery, and reset reveal neither account existence nor state, unknown accounts do
equivalent password work, operators can still tell the real cause internally, and no secret appears
in any response or log.

**Independent Test**: Compare login responses and verification work for unknown, wrong-password,
locked, and disabled accounts, compare recovery and reset responses, then scan every response and
captured log after exercising all flows.

### Tests for User Story 3

- [X] T022 [P] [US3] Add `tests/Authentication.IntegrationTests/Infrastructure/CountingPasswordHasher.cs`, a test-host decorator over `IPasswordHasher<ApplicationUser>` that counts `VerifyHashedPassword` calls, registered through the test factory without changing production code.
- [X] T023 [P] [US3] Add enumeration scenarios in `tests/Authentication.IntegrationTests/Scenarios/AntiEnumerationTests.cs`: login for an unknown account, a wrong password, a locked account, and a disabled account returns an identical status, message, and structure (trace id ignored) and exactly one `VerifyHashedPassword` call each, so work equivalence is measured by calls and not by duration; forgot-password and reset-password responses are identical for existing, unknown, and disabled accounts (equivalence when an internal step fails stays covered by the existing Phase 6 `PasswordRecoveryDeliveryFailureTests`, which T032 runs as regression); and each login failure leaves a `LoginFailed` event whose `Reason` names the real cause without the email or password (quickstart `AntiEnumerationTests`).
- [X] T024 [P] [US3] Add the consolidated secret scan in `tests/Authentication.IntegrationTests/Scenarios/SecretExposureTests.cs`: exercise login (success and failures), refresh, logout, change-password, forgot-password, reset-password, lockout, and rate limiting, then assert that no response body or header and no captured log contains a submitted password, a complete access token, a refresh credential, a reset token, a private-key PEM marker, the SMTP password, or an email address in a rate-limit event (quickstart `SecretExposureTests`).

### Implementation for User Story 3

- [X] T025 [US3] Run T023 and T024 and correct any divergence they expose in `src/Authentication.Infrastructure/Identity/IdentityCredentialValidator.cs`, `src/Authentication.Api/Features/PasswordRecovery/ForgotPasswordEndpoint.cs`, or `src/Authentication.Api/Features/PasswordRecovery/ResetPasswordEndpoint.cs` without altering any in-limit contract; the expected result is no production change because Phases 1–6 already provide this behavior.

**Checkpoint**: US3 confirms confidentiality of responses and logs; operators can distinguish causes through events only.

---

## Phase 6: Gate G7 Verification and Cross-Cutting Evidence

**Purpose**: Demonstrate the completed hardening over real Compose services, the reference proxy
boundary, and all Phase 1–6 regression behavior.

- [X] T026 [P] Add the reference first-layer proxy configuration `docs/reference-proxy/nginx.conf`: one `server` proxying `/api/auth/` and `/health` to `auth-api:8080`; `limit_req_zone $binary_remote_addr` with a first layer for `/api/auth/login`, `/refresh`, `/forgot-password`, and `/reset-password`, looser than the application (60 r/min, burst 20, `limit_req_status 429`); `proxy_set_header X-Forwarded-For $remote_addr` and `X-Forwarded-Proto $scheme` that overwrite and never append a client value; forward `Host`; `client_max_body_size 16k`. It documents the obligations of [configuration-and-proxy.md](contracts/configuration-and-proxy.md) and is not part of `compose.yml`.
- [X] T027 [P] Add the acceptance-only override `tests/acceptance/compose.reference-proxy.yml`: a `reference-proxy` service using the official `nginx` Alpine image with the tag and digest pinned when implemented, mounting T026, on a fixed subnet and fixed proxy address of the default network, and setting `ReverseProxy__TrustedProxies` on `auth-api` to that address only; never referenced by `compose.yml`.
- [X] T028 Add the Phase 7 Compose acceptance lifecycle in `tests/acceptance/phase-7.sh`, modeled on `tests/acceptance/phase-6.sh` and using the T027 override with disposable host directories: five failed logins lock an account and the correct password is refused; each policy returns an application `429` (`application/problem+json`) at a small configured limit while the others still answer, with a `Retry-After` that is positive and no longer than the configured window (no step sleeps or waits for a window to renew; [research.md](research.md) §4); a forged `X-Forwarded-For` sent directly is ignored (the log shows the real peer); through `reference-proxy` the log shows the client address and not the proxy or the forged value; the proxy's first layer returns its own `429` (HTML) at its larger limit; and the script finishes by tearing down its project and running `tests/acceptance/phase-6.sh` as regression (which chains Phases 5–1) in a subshell that unsets `COMPOSE_FILE` and every Phase 7 `AUTH_RATE_LIMIT_*`, `AUTH_LOCKOUT_*`, `AUTH_TRUSTED_*` variable, so the regression runs with its own generous limits and no proxy trust.
- [X] T029 Extend `tests/acceptance/phase-7.sh` to scan the accumulated `auth-api` logs for the submitted passwords, access and refresh values, the reset token, the SMTP password, and `PRIVATE KEY`, and to assert that the `LoginFailed`, `AccountLockedOut`, and `RateLimitApplied` events are present with UTC timestamps and trace identifiers.
- [X] T030 Create `docs/phase-7-operations.md` with the operator guidance: the lockout and request-limit settings with their documented defaults and the rationale and review note for those figures (a project decision, not normative), that limits live in process memory per instance and reset on restart, the trusted-proxy contract (including that an empty configuration disables forwarded-header processing and that `ASPNETCORE_FORWARDEDHEADERS_ENABLED` must not be set) and the proxy obligations from [configuration-and-proxy.md](contracts/configuration-and-proxy.md), how to use `docs/reference-proxy/nginx.conf`, the new security events and what each reveals, and the statement that complete production proxy deployment, TLS, and file logging belong to Phase 8; Gate G7 evidence is added in T032–T033.
- [X] T031 Verify the scope boundaries and record the results in `docs/phase-7-operations.md`: the diff against `main` adds no NuGet package or `PackageReference`; `compose.yml` still defines exactly `auth-api`, `api-a`, and `api-b`; `grep -rn -i -E "Redis|IDistributedCache|IMemoryCache|AddCors|UseCors" src --include='*.cs'` finds nothing; no migration was added (`dotnet ef migrations has-pending-model-changes` reports none, supplying `Microsoft.EntityFrameworkCore.Design` temporarily from the local package cache and reverting that change).
- [X] T032 Run `dotnet build --no-incremental -warnaserror` and the complete unit/integration suite, resolve Phase 7 and Phase 1–6 regressions, and record the exact commands and PASS evidence in `docs/phase-7-operations.md`, keeping `specs/007-phase-7-security-hardening/quickstart.md` as the reusable validation guide.
- [X] T033 Run `tests/acceptance/phase-7.sh` and record all five Gate G7 evidence states—build, tests, startup, feature, and regression—in `docs/phase-7-operations.md`, linked to the focused test and acceptance output.
- [ ] T034 After T001-T033, review implementation and evidence against Roadmap §13.10, present `docs/phase-7-operations.md` to the project owner, and obtain explicit Gate G7 approval. Only after that approval, update the authorized Phase 7 checklist/status/progress records in `baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md` and `specs/007-phase-7-security-hardening/checklists/requirements.md`, record the approval date/evidence reference without changing normative requirements, and create the identifiable Gate G7 closing commit; never mark Phase 7 complete or create the closing commit before approval.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1**: T001 has no dependencies; T002 may run in parallel with it; T003 follows T001 because
  it uses the variable names T001 maps.
- **Phase 2**: T004 first (independent of the rest); T005 and T006 in parallel; T007 depends on T005
  and T006; T008 depends on T007; T009 depends on T004, T005, and T008.
- **US1** depends on Phases 1–2 (T014 needs T008; T015 needs T006 and T007; T012 and T013 need T004).
- **US2** depends on Phase 2; T016 also needs T014, because the effective partition is observed
  through an attached policy's `429`; T019 depends on T018, T020 on T019 and T008.
- **US3** depends on US1 (events) and on T002; it may proceed in parallel with US2 once US1 is done.
- **Gate G7 verification** depends on all story phases; T027 depends on T026; T028 depends on T027
  and T003.

### User Story Completion Order

```text
Setup → Foundation → US1 → ┬→ US2 →┐
                           └→ US3 →┴→ Gate G7
```

### Parallel Opportunities

- T001/T002, T005/T006, T009, T010/T011, T016/T017, T018, T022/T023/T024, T026, and T027 (after
  T026) can run in parallel once their prerequisites are satisfied. T012 and T013 touch different files and may run
  together; T014 and T015 touch different endpoint files. `Program.cs` is edited only by T008 and
  T020, which run sequentially.

## Parallel Example: User Story 1

```text
Task: "Add lockout scenarios in tests/Authentication.IntegrationTests/Scenarios/AccountLockoutTests.cs"
Task: "Add rate-limit scenarios in tests/Authentication.IntegrationTests/Scenarios/RateLimitingTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete setup and the foundation (lockout defaults, options, limiter registration).
2. Complete US1 and prove lockout, the four endpoint limits, the per-address limit, and their
   independence.
3. Validate US1 independently before adding the proxy boundary.

### Incremental Delivery

1. US1 → brute-force and request-abuse protection.
2. US2 → trusted origin and verified browser boundary.
3. US3 → verified confidentiality and secret protection.
4. Gate G7 → Compose and reference-proxy acceptance plus Phase 1–6 regression.

## Notes

- Do not add a package, a migration, a service to `compose.yml`, a custom lockout counter or limiter
  algorithm, Redis or any external or database-backed counter, CORS, extra anti-CSRF machinery,
  production Nginx, TLS, file logging, or any Phase 8 capability.
- The default request-limit values are a project decision recorded in the plan; document them and
  keep them overridable rather than presenting them as normative.
- Keep passwords, tokens, cookies, reset tokens, keys, SMTP credentials, and email addresses of
  rate-limited requests out of logs, responses, and assertions.
- Do not change `plan.md`, the baseline documents, any closed phase record, or any Phase 1–6
  contract; tasks T021 and T025 are verification tasks whose expected result is no production change.

---

## Phase 7: Convergence

- [ ] T035 CRITICAL: Remove the window-renewal wait from `tests/acceptance/phase-7.sh` (the `sleep "$RETRY_AFTER"` step and its renewal assertion, plus the header comment claiming "window renewal after only the Retry-After"), and instead assert that every application `429` it checks carries a `Retry-After` that is positive and no longer than the configured window; then update the renewal statements in `docs/phase-7-operations.md` (validation note and the Gate G7 "Feature" row), re-run `tests/acceptance/phase-7.sh`, and refresh the recorded evidence per Constitution VI, spec NFR-002, and T028 (contradicts)
- [ ] T036 Extend `tests/Authentication.IntegrationTests/Scenarios/RateLimitingTests.cs` so that requests from `192.0.2.10` and `::ffff:192.0.2.10` are shown to share one allowance, and so that every `429` (the four policies and `forgot-password-address`) asserts a `Retry-After` that is positive and no longer than the configured window, not just its presence, per spec Edge Cases (IPv4/IPv6 single origin), NFR-002, and T011 (partial)
- [ ] T037 Add to `tests/Authentication.IntegrationTests/Scenarios/ForwardedHeadersTests.cs` the scheme and host scenario: run `ForwardedHeadersMiddleware` with the host's resolved `ForwardedHeadersOptions` over a `DefaultHttpContext`, and show that `X-Forwarded-Proto` changes the effective scheme only for a configured trusted peer (and never with an empty trusted set) and that `X-Forwarded-Host` never changes the host, per FR-008, US2/AC1, and T016 (partial)
- [ ] T038 Reconcile the Compose variable names: `compose.yml`, `.env.example`, the acceptance scripts, and `docs/phase-7-operations.md` use `AUTH_RATE_LIMIT_<POLICY>_PERMIT_LIMIT` / `_WINDOW_SECONDS` with `LOGIN`, `REFRESH`, `FORGOT_PASSWORD`, `RESET_PASSWORD`, `FORGOT_PASSWORD_ADDRESS`, while `specs/007-phase-7-security-hardening/contracts/configuration-and-proxy.md` names `AUTH_RATE_LIMIT_LOGIN_PERMITS`, `AUTH_RATE_LIMIT_FORGOT_PERMITS`, `AUTH_RATE_LIMIT_RESET_PERMITS`, `AUTH_RATE_LIMIT_FORGOT_ADDRESS_PERMITS`; update the contract's "Compose variables" table to the implemented names so a single set is documented, per T001 and plan: configuration contract (contradicts)
- [ ] T039 Add `AUTH_LOCKOUT_MAX_FAILED_ATTEMPTS`, `AUTH_LOCKOUT_DURATION`, `AUTH_TRUSTED_PROXIES`, and `AUTH_TRUSTED_NETWORKS` to the regression subshell `unset` list in `tests/acceptance/phase-7.sh`, so values inherited from the caller's environment cannot reach the Phase 6–1 regression, per T028 (partial)
- [ ] T040 Remove the unused `IsValid` property from `src/Authentication.Api/Security/RateLimitPolicy.cs`, or use it from `RateLimitingOptions.FirstInvalidSetting`, so the settings type carries no dead member, per Constitution IV (unrequested)
