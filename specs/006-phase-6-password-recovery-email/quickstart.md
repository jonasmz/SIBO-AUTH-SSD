# Quickstart: Validate Phase 6 Password Recovery, Reset and Email

This guide is the Gate G6 validation plan. It references the public contract in
[`contracts/authentication-api-password-recovery.openapi.yaml`](contracts/authentication-api-password-recovery.openapi.yaml)
and the state rules in [data-model.md](data-model.md). Executed evidence is recorded in
`docs/phase-6-operations.md`.

## 1. Prerequisites

- .NET SDK 10.0.112 (or a later 10.0 patch selected by `global.json`).
- Docker Engine with Compose, `curl`, `jq`, `openssl` for acceptance.
- Phase 1–5 configuration plus the new required settings:
  - `DataProtection__KeysPath` (Compose: bind mount from `AUTH_DATAPROTECTION_HOST_PATH`, owned by
    the container user, mode `0700`);
  - `Smtp__Host`, `Smtp__Port`, `Smtp__Security` (`None` | `StartTls` | `SslOnConnect`),
    `Smtp__Username` + `Smtp__Password` (both or neither), `Smtp__SenderAddress`,
    `Smtp__SenderName`.

No real SMTP server is needed for `dotnet test`. Acceptance starts a disposable Mailpit sink from
`tests/acceptance/compose.mail-sink.yml`; never point tests at a real mailbox.

## 2. Build and automated tests

```bash
dotnet build Authentication.slnx
dotnet test Authentication.slnx --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes \
  --project src/Authentication.Infrastructure --startup-project src/Authentication.Api
```

Expected: zero warnings, all tests pass, no pending model changes. Consolidated scenarios (NFR-001):

| # | Scenario | Expected |
|---|---|---|
| 1 | Forgot: enabled account, unknown address, disabled account | identical `204` (status, headers, empty body); fake sender called exactly once, only for the enabled account |
| 2 | Forgot: same address in different letter case | treated as the enabled account (one message) |
| 3 | Forgot: non-JSON, malformed, missing/blank/invalid email | `400 The request is invalid.`; no sender call |
| 4 | Forgot: real `SmtpEmailSender` against a refused local port | `204`; one `EmailDeliveryFailed` warning with exception type, host, port, UTC, trace id; no token, recipient or SMTP password in logs |
| 5 | Forgot: no session is revoked | all families still refresh |
| 6 | Reset: valid token (taken from the fake sender's message) + valid password | `204`; old password `401` at login; new password `200`; email, roles, enabled, lockout fields unchanged |
| 7 | Reset: garbage, one character altered, other account's token, same token reused after success, token issued before a Phase 5 password change | `401 Invalid or expired reset token.`; nothing changed |
| 8 | Reset: unknown email, disabled account (valid-looking token) | same `401` as scenario 7 |
| 9 | Reset: expired token (host with negative `TokenLifespan`, no waits) | `401`; nothing changed |
| 10 | Reset: valid token + policy-violating password | `400` policy detail; token still usable afterwards |
| 11 | Reset: non-JSON, malformed, missing/blank field | `400 The request is invalid.` |
| 12 | Reset with two active families | both refresh `401`; both rows `PasswordReset`; a failed reset (7–11) revokes none |
| 13 | Access token issued before the reset | still accepted by `ReferenceConsumer.Api` until expiry |
| 14 | Two concurrent resets with one token (`Task.WhenAll`) | exactly one `204`, one `401` |
| 15 | Password write fails (temporary `RAISE(ABORT)` trigger on `AspNetUsers`, then dropped) | `503`; old password works; token still usable; sessions intact |
| 16 | Revocation write fails (temporary trigger on `RenewableSessionFamilies`, then dropped) | `503`; old password works; both families still refresh |
| 17 | Restart: token issued, factory recreated on the same SQLite file and key directory | reset `204` |
| 18 | Negative control: factory recreated with a different key directory | reset `401` |
| 19 | Startup with missing/invalid `DataProtection__KeysPath` or any invalid `Smtp__*` | startup fails naming only the setting; no value echoed |
| 20 | Log and response scan over scenarios 1–18 | no token, password, hash, stamp, SMTP password or email body in any log or response |

Unit tests cover `SmtpOptions` validation and MIME message construction (sender, recipient, plain
text body containing the token). Phase 1–5 tests pass unchanged.

## 3. Manual smoke test (local, with Mailpit)

```bash
docker run --rm -d --name mail-sink -p 1025:1025 -p 8025:8025 \
  axllent/mailpit:v1.31.1@sha256:98b916bd3c8d61f7633a52d3ea2f58d00620cb01ca57ab59edde68c347a95365
# run auth-api with Smtp__Host=localhost Smtp__Port=1025 Smtp__Security=None ...
curl -s -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid"}' http://localhost:8080/api/auth/forgot-password   # 204
curl -s http://localhost:8025/api/v1/messages | jq '.messages[0].ID'                     # read the token
curl -s -o /dev/null -w '%{http_code}\n' -H 'Content-Type: application/json' \
  -d '{"email":"admin@local.invalid","token":"<token>","newPassword":"<new>"}' \
  http://localhost:8080/api/auth/reset-password                                           # 204
```

## 4. Acceptance (Compose)

```bash
tests/acceptance/phase-6.sh
```

`phase-6.sh` must, on disposable storage and with the mail-sink override: request recovery for an
existing account and for an unknown address and compare the responses; read the token from
Mailpit; establish two sessions; run `docker compose restart auth-api`, then
`docker compose up -d --force-recreate auth-api`, then `docker compose down -v` and `up` with the
same host directories; reset with the token (`204`); prove both sessions refresh `401`, the new
password logs in and the old one does not, and the token is rejected on reuse; prove the key-ring
directory is a host bind mount, is not a Compose volume, and has mode `0700`; scan `auth-api` logs
for the token, both passwords and the SMTP password; tear down; and finally run
`tests/acceptance/phase-5.sh`, which chains Phases 4–1 as regression. Phase 1–5 scripts export
disposable `AUTH_SMTP_*` and `AUTH_DATAPROTECTION_HOST_PATH` values. Expected: every script prints
PASS.

## 5. Gate G6 checklist

Record in `docs/phase-6-operations.md`: build, tests, startup, feature and Phase 1–5 regression all
PASS; complete forgot/reset; SMTP behind `IEmailSender` with MailKit only in Infrastructure
(`grep -rn MailKit src/Authentication.Domain src/Authentication.Application` returns nothing);
persistent key ring outside the Compose lifecycle; anti-enumeration; reset revokes all sessions;
`compose.yml` gains no service; no queue, retry, scheduler, template engine or token store; closing
commit.
