# Specification Quality Checklist: Phase 7 — Security Hardening

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-08
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Validation pass 1: all items pass except the intentional open markers. No library, class, or
  algorithm is prescribed; the endpoint paths named are the existing public contracts.
- Clarification session 2026-10-08 resolved both genuine ambiguities: documented, overridable
  defaults for the request limits fixed during planning (FR-019), and a documented reference proxy
  configuration verified once in a disposable acceptance proxy (FR-020).
- Gate G6 is closed in the roadmap; no dependency blocks this phase. The Phase 4 and Phase 6 notes
  that deferred refresh and recovery limiting are resolved by this phase.
- Most enumeration, cookie, and origin behavior already exists from Phases 1–6; the spec marks it
  as verified-only so the plan does not rebuild it.

## Gate G7 Closure

Closure recorded on 2026-10-08 after implementation tasks T001-T033 and convergence tasks T035-T040
were completed and verified and the convergence review found no remaining technical gaps.

Evidence: `docs/phase-7-operations.md` (Gate G7 verification evidence),
`tests/acceptance/phase-7.sh`, and the automated suite under `tests/`.

- [x] Complete Identity lockout: 5 consecutive failures and 15 minutes by default, externally configurable, counted only by Identity, generic response while locked
- [x] Complete rate limiting: independent per-origin policies for login, refresh, forgot-password and reset-password with `429` ProblemDetails, plus the per-normalized-address recovery limit, all in process memory and independent of lockout
- [x] Proxy trust configured: forwarded headers honored only from explicitly configured proxies or networks, none without configuration; reference Nginx first layer verified in a disposable acceptance proxy
- [x] Secure cookie: `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, `Secure` in production, cleared with the same attributes on logout
- [x] CSRF/origin covered: refresh and logout accept only the configured frontend origin; no CORS enabled
- [x] Anti-enumeration covered: identical login failures with equivalent verification work; forgot/reset reveal no account state
- [x] Secret-free logs with UTC `LoginFailed`, `AccountLockedOut` and `RateLimitApplied` events
- [x] Build PASS (`dotnet build --no-incremental -warnaserror`: 0 warnings, 0 errors)
- [x] Tests PASS (181/181, 0 skipped)
- [x] Regression Phase 1-6 PASS (`phase-7.sh` runs `phase-6.sh`, which chains Phases 5, 4, 3, 2 and 1)
- [x] Checklist updated (this section)
- [x] Gate G7 explicitly approved by the project owner on 2026-10-08
- [x] Identifiable Phase 7 closing commit created (`[Phase 7] Close Gate G7`)
