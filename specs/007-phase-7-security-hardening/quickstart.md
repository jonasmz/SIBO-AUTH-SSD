# Quickstart: Phase 7 Validation

## Prerequisites

.NET 10 SDK (`global.json`), Docker with Compose, `curl`, `jq`, `openssl`.

## Automated suite

```bash
dotnet build Authentication.slnx -warnaserror
dotnet test
```

Expected consolidated scenarios (see [plan.md](plan.md) file list):

| Scenario | Proves |
|---|---|
| `AccountLockoutTests` | 5 failures lock; correct password refused with the generic 401 while locked; `LockoutEnd` ≈ now+15 min; moving `LockoutEnd` to the past re-enables login; success resets the counter; `AccountLockedOut` event |
| `RateLimitingTests` | each of the four policies returns 429 ProblemDetails once exceeded from one address while the others answer normally; address limit across varying addresses/letter case for existing and unknown emails; 400 consumes no address allowance; limited requests do not touch lockout; locked account judged only by lockout |
| `ForwardedHeadersTests` | forged `X-Forwarded-For` from an untrusted peer has no effect (rotating it does not reset a limit); same header from a configured proxy/network does; empty set honors nothing; untrusted hops before the trusted one ignored |
| `BrowserBoundaryTests` | cookie attributes (Secure in Production), logout clear, foreign/missing/malformed origin refused, no CORS headers |
| `AntiEnumerationTests` | identical 401 for unknown/wrong/locked/disabled; one hash verification each; forgot/reset reveal nothing |
| `SecretExposureTests` | all flows exercised, responses and captured logs free of passwords, tokens, cookies, reset tokens, PEM, SMTP password |
| `SecurityConfigurationTests` | defaults without settings, overrides applied, invalid values fail startup |

No test waits for wall-clock time ([research.md §4](research.md)).

## Acceptance (disposable)

```bash
tests/acceptance/phase-7.sh
```

Expected `PASS` lines: lockout over Compose; app 429 (`application/problem+json`) per policy with
a `Retry-After` no longer than the configured window (no step sleeps); direct forged
`X-Forwarded-For` ignored (log shows the real peer); through `reference-proxy` the log shows the
client address, not the proxy or the forged value; Nginx first-layer 429 (HTML) observed; no
secrets in `auth-api` logs; Phase 6→1 regression with lifted limits. Teardown removes all
disposable state.

## Gate G7

Record evidence in `docs/phase-7-operations.md` against Roadmap §13.10.
