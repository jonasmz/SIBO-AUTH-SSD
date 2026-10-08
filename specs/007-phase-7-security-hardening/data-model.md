# Data Model: Phase 7 — Security Hardening

No persisted entity, column, index or migration is added. Lockout uses the Identity columns that
already exist on `AspNetUsers`; everything else is configuration or in-memory runtime state.

## Lockout state (existing, Identity-owned)

| Field (`AspNetUsers`) | Meaning | Written by |
|---|---|---|
| `AccessFailedCount` | Consecutive failed password checks | `AccessFailedAsync` (login wrong password; change-password wrong current password) |
| `LockoutEnd` (UTC) | Locked until this instant when in the future | `AccessFailedAsync` when the count reaches `MaxFailedAccessAttempts` |
| `LockoutEnabled` | Lockout applies to the account | `AllowedForNewUsers = true` at creation (existing accounts already `true`) |

**Transitions** (Identity behavior, unchanged):

```text
count n < max-1 --wrong password--> count n+1
count max-1     --wrong password--> LockoutEnd = now + span, count 0   → event AccountLockedOut
locked          --any password-----> refused, state unchanged          → event LoginFailed(LockedOut)
locked, LockoutEnd <= now --correct--> authenticated, count reset to 0
any             --correct (enabled)-> count 0
disabled        --correct password-> refused, count unchanged          → event LoginFailed(Disabled)
disabled        --wrong password---> same as the wrong-password rows    → event LoginFailed(WrongPassword)
```

Request limiting never reads or writes these fields.

## Lockout options (configuration)

| Key | Type | Default | Validation |
|---|---|---|---|
| `Identity:Lockout:MaxFailedAccessAttempts` | int | 5 | `>= 1`, else startup fails |
| `Identity:Lockout:DefaultLockoutTimeSpan` | TimeSpan (`hh:mm:ss`) | `00:15:00` | `> 0`, else startup fails |

## Request-limit policy (configuration + memory)

| Attribute | Rule |
|---|---|
| Name | `login`, `refresh`, `forgot-password`, `reset-password`, `forgot-password-address` |
| PermitLimit | positive int; default per [contracts/configuration-and-proxy.md](contracts/configuration-and-proxy.md) |
| WindowSeconds | positive int; same |
| Partition | effective client address (first four); normalized email (`forgot-password-address`) |
| Algorithm | fixed window, `QueueLimit = 0`, no queueing |
| Storage | process memory; lost on restart; per instance |

**Transitions per partition**: `permits available --request--> permits-1`; `0 permits --request-->
429 (no handler execution)`; `window elapsed --> permits = PermitLimit`.

## Trusted proxy set (configuration)

| Key | Format | Default | Validation |
|---|---|---|---|
| `ReverseProxy:TrustedProxies` | comma-separated IP addresses | empty | each entry parses as `IPAddress`, else startup fails |
| `ReverseProxy:TrustedNetworks` | comma-separated CIDR | empty | each entry parses as `IPNetwork`, else startup fails |

Empty both → no forwarded header is honored.

## Effective client origin (per request, derived)

`Connection.RemoteIpAddress` and `Request.Scheme` after `UseForwardedHeaders`: replaced by the
rightmost `X-Forwarded-For`/`X-Forwarded-Proto` entries only while the current peer is in the
trusted set. IPv4-mapped IPv6 is folded to IPv4 when used as a partition key. Consumers: rate-limit
partition, `RateLimitApplied` event.

## Security events (logs)

| Event | Level | Fields | Never contains |
|---|---|---|---|
| `LoginFailed` | Warning | `Reason`, `UserId?`, `OccurredAtUtc`, `TraceId`, `SpanId` | email, password |
| `AccountLockedOut` | Warning | `UserId`, `LockoutEndUtc`, `Source`, `OccurredAtUtc`, `TraceId`, `SpanId` | password |
| `RateLimitApplied` | Warning | `Policy`, `ClientAddress?`, `OccurredAtUtc`, `TraceId`, `SpanId` | email (address policy logs no address at all) |

Existing Phase 3–6 events are preserved unchanged.
