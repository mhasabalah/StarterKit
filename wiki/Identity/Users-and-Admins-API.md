# Identity HTTP API: auth, users and admins

This reference was generated from the endpoint classes under `src/Modules/Identity/Identity/Domains/**/Features/`.
**The code is the source of truth.** Regenerate this table when routes change, and check `/swagger` or `/scalar` for
request and response shapes.

Every response, including errors, uses the `ApiResponse<T>` envelope (`isSuccess`, `statusCode`, `data`, `errors[]`,
`traceId`). Permissions are explained in
[Permissions-2FA-and-Admin-Audit.md](Permissions-2FA-and-Admin-Audit.md). Tokens carry roles, and permissions are
derived per request. The rate-limit policies (`auth`, `otp`, `search`) are configured in `RateLimiting:Policies`.

## Authentication (`api/auth/*`)

| Method | Route | Auth | Rate limit | Notes |
|---|---|---|---|---|
| POST | `api/auth/register` | anonymous | auth | Email/password registration |
| POST | `api/auth/login` | anonymous | auth | Returns access + refresh token, or a 2FA challenge |
| POST | `api/auth/2fa/verify` | anonymous | auth | Completes a 2FA login challenge (TOTP or recovery code) |
| POST | `api/auth/refresh` | anonymous | auth | Rotating refresh token; reuse of a revoked token is detected |
| POST | `api/auth/logout` | signed in | - | Ends the caller's **session**: revokes its refresh-token family and blacklists its `sid`, so every access token of that session stops working at once (not only the presented one). Also blacklists the presented access token and revokes the presented refresh token. `logoutAllDevices: true` ends every session. |
| POST | `api/auth/change-password` | signed in | auth | |
| POST | `api/auth/forgot-password` | anonymous | otp | Sends a reset OTP (always 200, so accounts can't be enumerated) |
| POST | `api/auth/verify-reset-otp` | anonymous | auth | |
| POST | `api/auth/reset-password` | anonymous | auth | |
| POST | `api/auth/email/send-confirmation` | signed in | otp | |
| POST | `api/auth/email/confirm` | anonymous | auth | |
| POST | `api/auth/2fa/setup` · `enable` · `disable` · `recovery-codes/regenerate` | signed in | auth | TOTP authenticator lifecycle |
| POST | `api/auth/switch-tenant` | signed in, host only | auth | Multi-tenancy: re-issue tokens for another membership |

## Onboarding, profile and verification

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| POST | `/api/identity/onboarding/register` | anonymous | otp |
| POST | `/api/identity/onboarding/resend-otp` | anonymous | otp |
| POST | `/api/identity/onboarding/verify-phone` | anonymous | auth |
| GET | `api/identity/profile` | signed in | - |
| PUT | `/api/identity/profile/complete` | signed in | - |
| GET | `api/identity/profile/export` | signed in | auth |
| POST | `api/identity/profile/delete-code` | signed in, not impersonating | otp |
| DELETE | `api/identity/profile` | signed in, not impersonating | auth |
| POST | `/api/identity/phone/send-verification` | signed in | otp |
| POST | `/api/identity/phone/verify` | signed in | auth |
| POST | `/notifications/verification/send` | signed in | otp |
| POST | `/notifications/verification/validate` | signed in | auth |
| GET | `/api/identity/auth/external-login`, `/api/identity/auth/challenge/{provider}`, `/api/identity/auth/external-callback` | anonymous (login flow) | callback: auth |

### Account deletion and data export (store / GDPR)

`DELETE api/identity/profile` (body `{ "password": "..." }` or `{ "otpCode": "..." }`) erases the caller's own account.
It is irreversible, rejects impersonation tokens and is rate limited.

- **Re-authentication.** Accounts with a password must send it (a wrong one counts as a failed access attempt). Phone-only
  accounts call `POST api/identity/profile/delete-code` first (SMS, `OtpPurpose.AccountDeletion`) and send the code.
- **Refused with 409** while the user owns a tenant (Tenancy `GetOwnedTenantsQuery`; transfer ownership first) or holds
  an admin role (an admin is demoted first, as for the staff delete).
- **Effect (one unit of work).** All sessions end (stamp rotated, refresh tokens deleted, access tokens blacklisted).
  External logins, claims, roles, 2FA key and recovery codes and pending OTPs are deleted. The user row is kept for
  referential integrity and financial records but anonymized in place: user name and e-mail become
  `deleted-<id>` / `deleted-<id>@deleted.invalid`, phone, names, avatar and OAuth ids are cleared, the password hash is
  removed, the account is deactivated and locked forever. An `AccountDeletion` tombstone (no PII) and an audit entry
  `account.erased` (actor = target, no snapshot, no IP / user agent) are saved with it.
- **Fan-out.** The tombstone raises `UserDeletedIntegrationEvent` (`userdirectory.user-deleted.v1`, ids only) through the
  Identity outbox, in the same transaction. Consumers, each idempotent: Attachment (`UserDeletedAttachmentConsumer`)
  hides and deletes every file the user uploaded, in every tenant, through the normal deletion job; Tenancy
  (`UserDeletedTenancyConsumer`) removes the user's memberships; Notification (`UserDeletedNotificationConsumer`)
  deletes the delivery log and preferences. New modules holding personal data subscribe to the same event.
- **Not erased.**
  - Notification logs sent with `.ToEmail(addr)` and **no user id** (for example mail to an address that is not tied to
    an account) are not found by `UserDeletedNotificationConsumer`, which deletes by user id. Prefer `.For(userId)` for
    anything personal, or purge by address in your own job.
  - Earlier identity audit rows about the user are **retained**: the table is append-only by design and holds admin-action
    snapshots (`user.edited`, `user.status_changed`, ...). Tenant audit trails also keep their ids.
  - Retention basis to state in the privacy policy: security and fraud-prevention records kept for the legitimate
    interest of protecting accounts and for accountability of administrative actions (GDPR Art. 6(1)(f)); the rows hold
    ids and action metadata, not the erased profile data. State the retention period you apply.

`GET api/identity/profile/export` returns the caller's Identity data as JSON: profile, contact, roles, external login
providers, security flags, active sessions (created / expires / IP / tenant) and the last 100 identity audit entries about
them. No secrets. It covers Identity only; other modules can add their own sections later (extend `MyDataExportDto`
or add a sibling query).

## User management (platform staff)

| Method | Route | Permission |
|---|---|---|
| GET | `api/identity/users` | `users.read` |
| GET | `api/identity/users/{id}` | `users.read` |
| PUT | `api/identity/users/{id}` | `users.manage` |
| PATCH | `api/identity/users/{id}/status` | `users.manage` |
| POST | `api/identity/users/{id}/lock` · `unlock` | `users.manage` |
| POST | `api/identity/users/{id}/roles` | `users.manage` |
| DELETE | `api/identity/users/{id}/roles/{roleName}` | `users.manage` |
| POST | `api/identity/users/{id}/2fa/reset` | `admins.manage` |
| POST | `api/identity/users/{id}/promote-admin` | `admins.manage` |
| DELETE | `api/identity/users/{id}` | `SuperAdminOnly` policy |

## Admin management

| Method | Route | Permission |
|---|---|---|
| GET | `api/identity/admins` | `admins.manage` |
| GET | `api/identity/admins/{id}` | `admins.manage` |
| POST | `api/identity/admins` | `admins.manage` |
| PUT | `api/identity/admins/{id}` | `admins.manage` |
| PUT | `api/identity/admins/{id}/change-password` | `admins.manage` |
| POST | `api/identity/admins/{id}/demote` | `admins.manage` |

## Governance and support

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/admin/identity/audit` | `audit.read`, rate limit `search` | Admin action audit log |
| POST | `api/identity/impersonations` | host only + `tenants.impersonate`, rate limit `auth` | Short, audited, read-mostly support session. The token carries `act`; endpoints with `.DisallowImpersonation()` refuse it. The audit is written in-process before the token is issued (`ImpersonationStartedEvent`). |

## Default role → permission grants

From `Shared.Core/Authorization/RolePermissionMap.cs`:

| Role | Permissions |
|---|---|
| SuperAdmin | all platform + all tenant permissions |
| Admin | all except `admins.manage` (+ all tenant permissions) |
| Finance | `billing.*`, `plans.manage`, `payment-settings.manage`, `audit.read` |
| Support | `billing.read`, `users.read`, `tenants.read`, `tenants.impersonate` |
| User | none |

Privileged roles must have 2FA enabled.

## Regenerating this page

```bash
grep -rhoE 'Map(Get|Post|Put|Delete|Patch)\((EndpointConfig\.BaseApiPath \+ )?"[^"]+"' --include=*.cs src/Modules/Identity/Identity | sort -u
```

## Sessions (devices)

A session is one refresh-token family. Its id (`FamilyId`) is carried in the access token as the `sid` claim.
Revoking a session revokes the family and blacklists the `sid` in Redis for the access-token lifetime, so that
session's live access token is rejected immediately (fails open while Redis is down, like the token blacklist).

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `api/v1/me/sessions` | signed in | Active sessions with device, user agent, IP, last used; the current one is flagged |
| DELETE | `api/v1/me/sessions/{id}` | signed in, no impersonation | Unknown and foreign ids both return 404 |
| POST | `api/v1/me/sessions/revoke-others` | signed in, no impersonation | Keeps only the current session |
| GET | `api/v1/admin/users/{id}/sessions` | `sessions.manage` | Support/Admin/SuperAdmin; staff accounts only by SuperAdmin |
| DELETE | `api/v1/admin/users/{id}/sessions/{sessionId}` | `sessions.manage`, no impersonation | Audited |

## API keys (personal access tokens)

Format `sk_<8-char prefix>_<secret>`, returned **once** on create; only a SHA-256 hash is stored. Send it as
`Authorization: ApiKey <key>` or `X-Api-Key: <key>`. A policy scheme picks Bearer or ApiKey per request.
Effective permissions = the owner's current role/tenant permissions **∩** the key's scopes; a key can never exceed its
owner, and stops working when the owner is locked, inactive or deleted. Endpoints marked `.DisallowApiKey()`
(impersonation, 2FA, password, switch-tenant, logout, account deletion/export, session and API-key management) reject
key callers. A key principal carries **no** `ClaimTypes.Role` and no `is_admin` claim (so `IsInRole`, AdminOnly, Hangfire
and "is admin" checks never match a key); the owner's roles travel as `ApiKeyClaimTypes.OwnerRole`, read only by
`PermissionClaimsTransformation` (and the MFA gate) to derive permissions before the scope intersection. Each key has its own rate-limit partition. Config: `Identity:ApiKeys` (default expiry 90 days, max 365,
10 active keys per user).

| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `api/v1/me/api-keys` | signed in, no impersonation | Scopes must be permissions the owner holds now |
| GET | `api/v1/me/api-keys` | signed in | Prefix, name, scopes, expiry, last used (never the secret) |
| DELETE | `api/v1/me/api-keys/{id}` | signed in, no impersonation | |
| GET | `api/v1/admin/users/{id}/api-keys` | `api-keys.manage` | Admin/SuperAdmin |
| DELETE | `api/v1/admin/users/{id}/api-keys/{keyId}` | `api-keys.manage`, no impersonation | Audited |

Known limit: a key owned by an account that must use 2FA gets `mfa_required` where 2FA is enforced (key principals
carry no `amr` claim).
