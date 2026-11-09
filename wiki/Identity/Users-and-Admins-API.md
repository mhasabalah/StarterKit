# Identity HTTP API

This reference lists the Identity Carter routes, their declared authorization and their purpose so clients and reviewers can check the HTTP boundary.

## At a glance

Routes are verified against `src/Modules/Identity/Identity/Domains/**/Features/` endpoint classes and grouped modules.
Versioned routes below use v1; `EndpointConfig.BaseApiPath` is `api/v{version:apiVersion}`. Guid constraints are omitted
from displayed URLs. Use `/swagger` or `/scalar` for full request and response schemas.

## Authorization and response conventions

- **Signed in** means explicit `RequireAuthorization()`. Permission strings mean `RequirePermission`, with the
  configured MFA requirement. `SuperAdminOnly` is a role policy. Handler-level ownership and hierarchy checks still apply.
- **No key** means `DisallowApiKey()`; **no impersonation** means explicit `DisallowImpersonation()` metadata.
  In addition, the impersonation guard blocks all unsafe Identity methods and unsafe host-only endpoints, even
  where the table has no explicit no-impersonation marker. GET, HEAD and OPTIONS are safe methods.
- **Host only** suppresses ambient tenant resolution; it does not mean SuperAdmin.
- Tenant SSO/SCIM administration uses `tenant.sso.manage`, `RequireFeature("sso")` and no-key restrictions.
  Feature enforcement depends on the entitlement configuration; see [Enterprise SSO](Enterprise-SSO.md).
- SCIM protocol endpoints use the dedicated `scim` policy and directory bearer token, not ordinary JWTs or API keys.
- Rate names are `RateLimiting:Policies` policies. Human verification is conditional on
  [bot protection](Bot-Protection.md), not an authentication substitute.

REST JSON endpoints generally use [ApiResponse](../Architecture/Errors-and-Responses.md). Redirects, SAML metadata
and [SCIM protocol responses](SCIM.md) are exceptions; do not unwrap every Identity response as JSON `data`.

## Authentication

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/change-password` | Signed in; no key | Change the signed-in user's password; rate `auth` |
| PUT | `/api/identity/profile/complete` | Signed in | Complete Profile |
| POST | `/api/auth/email/confirm` | Anonymous | Confirm an email with the user Id + token from the confirmation link; rate `auth` |
| POST | `/api/auth/2fa/disable` | Signed in; no key | Disable two-factor authentication; rate `auth` |
| POST | `/api/auth/2fa/enable` | Signed in; no key | Enable two-factor authentication; rate `auth` |
| GET | `/api/identity/auth/external-login` | Anonymous | External Login; rate `auth` |
| GET | `/api/identity/auth/challenge/{provider}` | Anonymous | External Login Challenge; rate `auth` |
| GET | `/api/identity/auth/external-callback` | Anonymous | External Login Callback; rate `auth` |
| POST | `/api/auth/forgot-password` | Anonymous | Request password reset; rate `otp`; human verification `forgot-password` |
| POST | `/api/auth/login` | Anonymous | Login user; rate `auth`; human verification `login` |
| POST | `/api/auth/logout` | Signed in; no key | Logout and invalidate tokens |
| POST | `/api/auth/refresh` | Anonymous | Refresh access token; rate `auth` |
| POST | `/api/auth/2fa/recovery-codes/regenerate` | Signed in; no key | Regenerate two-factor recovery codes; rate `auth` |
| POST | `/api/auth/register` | Anonymous | Register new user; rate `auth`; human verification `register` |
| POST | `/api/auth/reset-password` | Anonymous | Reset password; rate `auth` |
| POST | `/api/auth/email/send-confirmation` | Signed in | Send (or resend) the email confirmation link to the signed-in user; rate `otp` |
| POST | `/api/identity/phone/send-verification` | Signed in | Send a verification code to the signed-in user's phone; rate `otp` |
| POST | `/api/auth/2fa/setup` | Signed in; no key | Start two-factor enrollment; rate `auth` |
| POST | `/api/auth/switch-tenant` | Signed in; host only; no key | Switch tenant; rate `auth` |
| POST | `/api/identity/phone/verify` | Signed in | Verify the signed-in user's phone with the code that was sent; rate `auth` |
| POST | `/api/auth/verify-reset-otp` | Anonymous | Verify phone OTP for password reset; rate `auth` |
| POST | `/api/auth/2fa/verify` | Anonymous; no key | Complete a login with the second factor; rate `auth` |

## Onboarding

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/identity/onboarding/register` | Anonymous | Onboarding Register; rate `otp`; human verification `onboarding-register` |
| POST | `/api/identity/onboarding/resend-otp` | Anonymous | Onboarding Resend Otp; rate `otp`; human verification `resend-otp` |
| POST | `/api/identity/onboarding/verify-phone` | Anonymous | Onboarding Verify Phone; rate `auth` |

## Profile and user management

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| DELETE | `/api/identity/profile` | Signed in; no key; no impersonation | Delete my account (irreversible); rate `auth` |
| POST | `/api/identity/profile/delete-code` | Signed in; no key; no impersonation | Send the SMS code that confirms account deletion (password-less accounts); rate `otp` |
| DELETE | `/api/identity/users/{id}` | `SuperAdminOnly` | Delete a user account (soft delete) |
| PUT | `/api/identity/users/{id}` | `users.manage` | Edit user profile |
| GET | `/api/identity/profile/export` | Signed in; no key | Export my personal data; rate `auth` |
| GET | `/api/identity/profile` | Signed in | Get my profile |
| GET | `/api/identity/users/{id}` | `users.read` | Get user details by ID |
| GET | `/api/identity/users` | `users.read` | Get Users |
| POST | `/api/identity/users/{id}/lock` | `users.manage` | Lock/suspend a user account |
| POST | `/api/identity/users/{id}/2fa/reset` | `admins.manage`; no key | Reset a user's two-factor authentication |
| POST | `/api/identity/users/{id}/unlock` | `users.manage` | Unlock/reactivate a user account |
| PATCH | `/api/identity/users/{id}/status` | `users.manage` | Update user account status |

## Admin management and role assignment

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/identity/users/{id}/roles` | `users.manage` | Assign a role to a user |
| PUT | `/api/identity/admins/{id}/change-password` | `admins.manage`; no key | Change admin password |
| POST | `/api/identity/admins` | `admins.manage` | Create a new admin account |
| POST | `/api/identity/admins/{id}/demote` | `admins.manage` | Demote an admin to regular user |
| GET | `/api/identity/admins/{id}` | `admins.manage` | Get admin details by ID |
| GET | `/api/identity/admins` | `admins.manage` | Get Admins |
| POST | `/api/identity/users/{id}/promote-admin` | `admins.manage` | Promote a user to admin |
| DELETE | `/api/identity/users/{id}/roles/{roleName}` | `users.manage` | Revoke a role from a user |
| POST | `/api/identity/impersonations` | `tenants.impersonate`; host only; no key | Impersonate a tenant member (support); rate `auth` |
| PUT | `/api/identity/admins/{id}` | `admins.manage` | Update admin profile |

## Admin audit

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/admin/identity/audit` | `audit.read` | Get the Identity admin audit log; rate `search` |

## Sessions (devices)

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/me/sessions` | Signed in; no key | List my active sessions |
| DELETE | `/api/v1/me/sessions/{id}` | Signed in; no key; no impersonation | Sign one of my devices out |
| POST | `/api/v1/me/sessions/revoke-others` | Signed in; no key; no impersonation | Sign out of all other devices |
| GET | `/api/v1/admin/users/{id}/sessions` | `sessions.manage`; no key | List a user's active sessions |
| DELETE | `/api/v1/admin/users/{id}/sessions/{sessionId}` | `sessions.manage`; no key; no impersonation | Sign one of a user's devices out |

## API keys (personal access tokens)

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/me/api-keys` | Signed in; no key; no impersonation | Create an API key; rate `create` |
| GET | `/api/v1/me/api-keys` | Signed in; no key | List my API keys |
| DELETE | `/api/v1/me/api-keys/{id}` | Signed in; no key; no impersonation | Revoke one of my API keys |
| GET | `/api/v1/admin/users/{id}/api-keys` | `api-keys.manage`; no key | List a user's API keys |
| DELETE | `/api/v1/admin/users/{id}/api-keys/{keyId}` | `api-keys.manage`; no key; no impersonation | Revoke a user's API key |

## Notification-delivered verification

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/notifications/verification/send` | Signed in | Send a verification code; rate `otp` |
| POST | `/api/v1/notifications/verification/validate` | Signed in | Validate a verification code; rate `auth` |

## Passkeys

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/passkeys/registration/options` | Signed in; no key; no impersonation | Start adding a passkey; rate `auth` |
| POST | `/api/auth/passkeys/registration` | Signed in; no key; no impersonation | Finish adding a passkey; rate `auth` |
| GET | `/api/auth/passkeys` | Signed in; no key | List my passkeys |
| PATCH | `/api/auth/passkeys/{id}` | Signed in; no key; no impersonation | Rename one of my passkeys |
| DELETE | `/api/auth/passkeys/{id}` | Signed in; no key; no impersonation | Remove one of my passkeys |
| POST | `/api/auth/passkeys/assertion/options` | Anonymous | Start a passkey sign-in; rate `auth` |
| POST | `/api/auth/passkeys/assertion` | Anonymous | Finish a passkey sign-in; rate `auth` |

## Enterprise SSO

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/tenant/sso/connections` | `tenant.sso.manage`; `sso` feature; no key | List SSO connections |
| GET | `/api/v1/tenant/sso/connections/{id}` | `tenant.sso.manage`; `sso` feature; no key | SSO connection |
| POST | `/api/v1/tenant/sso/connections` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Create an SSO connection (draft) |
| PUT | `/api/v1/tenant/sso/connections/{id}` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Update an SSO connection |
| POST | `/api/v1/tenant/sso/connections/{id}/activate` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Activate an SSO connection |
| POST | `/api/v1/tenant/sso/connections/{id}/disable` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Disable an SSO connection |
| DELETE | `/api/v1/tenant/sso/connections/{id}` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Delete an SSO connection |
| POST | `/api/v1/tenant/sso/connections/{id}/refresh-metadata` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Refresh the SAML metadata now; rate `create` |
| POST | `/api/v1/tenant/sso/connections/{id}/domains` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Add an email domain |
| POST | `/api/v1/tenant/sso/connections/{id}/domains/{domain}/verify` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Verify an email domain (DNS TXT); rate `create` |
| DELETE | `/api/v1/tenant/sso/connections/{id}/domains/{domain}` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Remove an email domain |
| POST | `/api/v1/tenant/sso/connections/{id}/test` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Test an SSO connection |
| GET | `/api/v1/tenant/sso/test-results/{code}` | `tenant.sso.manage`; `sso` feature; no key | Result of an SSO connection test |
| GET | `/api/v1/admin/sso/connections` | `tenants.read` | All SSO connections (platform) |
| POST | `/api/auth/sso/discover` | Anonymous | Home-realm discovery; rate `auth` |
| GET | `/api/auth/sso/{connectionId}/start` | Anonymous | Start SSO sign-in (browser navigation); rate `auth` |
| GET | `/api/auth/sso/oidc/callback` | Anonymous | OIDC redirect URI; rate `auth` |
| POST | `/api/auth/sso/saml/{connectionId}/acs` | Anonymous | SAML Assertion Consumer Service (HTTP-POST); rate `auth` |
| GET | `/api/auth/sso/saml/{connectionId}/metadata` | Anonymous | SAML service-provider metadata |
| POST | `/api/auth/sso/exchange` | Anonymous | Exchange the SSO code for a session; rate `auth` |

## SCIM

| Verb | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/tenant/scim/directories` | `tenant.sso.manage`; `sso` feature; no key | List SCIM directories |
| POST | `/api/v1/tenant/scim/directories` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Create a SCIM directory |
| POST | `/api/v1/tenant/scim/directories/{id}/rotate-token` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Rotate the SCIM token |
| PUT | `/api/v1/tenant/scim/directories/{id}/enabled` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Enable or disable a SCIM directory |
| DELETE | `/api/v1/tenant/scim/directories/{id}` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Delete a SCIM directory |
| GET | `/api/v1/tenant/scim/directories/{id}/groups` | `tenant.sso.manage`; `sso` feature; no key | Groups pushed by the directory |
| PUT | `/api/v1/tenant/scim/directories/{id}/groups/{groupId}/role` | `tenant.sso.manage`; `sso` feature; no key; no impersonation | Map a directory group to a tenant role |
| GET | `/scim/v2/ServiceProviderConfig` | `scim` policy (directory token) | SCIM capabilities |
| GET | `/scim/v2/ResourceTypes` | `scim` policy (directory token) | SCIM resource types |
| GET | `/scim/v2/Schemas` | `scim` policy (directory token) | SCIM schemas |
| GET | `/scim/v2/Users` | `scim` policy (directory token) | List / filter users |
| GET | `/scim/v2/Users/{id}` | `scim` policy (directory token) | Get a user |
| POST | `/scim/v2/Users` | `scim` policy (directory token) | Provision a user |
| PUT | `/scim/v2/Users/{id}` | `scim` policy (directory token) | Replace a user |
| PATCH | `/scim/v2/Users/{id}` | `scim` policy (directory token) | Patch a user (active=false deprovisions) |
| DELETE | `/scim/v2/Users/{id}` | `scim` policy (directory token) | Deprovision a user |
| GET | `/scim/v2/Groups` | `scim` policy (directory token) | List / filter groups |
| GET | `/scim/v2/Groups/{id}` | `scim` policy (directory token) | Get a group |
| POST | `/scim/v2/Groups` | `scim` policy (directory token) | Create a group |
| PUT | `/scim/v2/Groups/{id}` | `scim` policy (directory token) | Replace a group |
| PATCH | `/scim/v2/Groups/{id}` | `scim` policy (directory token) | Patch a group (members add/remove) |
| DELETE | `/scim/v2/Groups/{id}` | `scim` policy (directory token) | Delete a group |

## Usage and operational behavior

Login uses `identifier` (email or phone) and `password`. A 200 can return tokens, a second-factor challenge or a
partial login; see the [JWT frontend contract](jwt/JWT-AUTH-FRONTEND-GUIDE.md). Unknown accounts, invalid passwords
and lockout share a 401. Lockout defaults are under `IdentitySettings:Lockout` (5 failures, 15 minutes).
Forgot-password returns a generic success for unknown/ineligible accounts; validation, rate limits and delivery
failures can still produce other HTTP outcomes. Email sends a reset link; verified phone uses an OTP.

Session lists expose device, user agent, IP and last use, flagging the current family. Session revocation returns
404 for unknown, foreign or already-ended families. Staff-session administration also checks the target hierarchy.
[JWT lifecycle](jwt/JWT-AUTH-TECHNICAL-GUIDE.md) owns logout and revocation guarantees.

API-key creation reveals the secret once; list responses show only metadata (prefix, name, scopes, expiry and last
use). Scopes cannot exceed owner permissions. [Identity](README.md#api-keys-personal-access-tokens) owns key
authentication and configuration. [Governance](Permissions-2FA-and-Admin-Audit.md) owns role grants and audit.

### Account deletion and data export (store / GDPR)

`DELETE api/identity/profile` (body `{ "password": "..." }` or `{ "otpCode": "..." }`) erases the caller's own account.
It is irreversible, rejects impersonation tokens and API keys, and is rate limited.

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
  - Document the retention period and applicable basis in the deployment privacy policy. Account erasure does not purge append-only audit history.

`GET api/identity/profile/export` returns the caller's Identity data as JSON: profile, contact, roles, external login
providers, security flags, active sessions (created / expires / IP / tenant) and the last 100 identity audit entries about
them. No secrets. It covers Identity only; other modules can add their own sections later (extend `MyDataExportDto`
or add a sibling query).

## Failure modes and guarantees

Authentication failures return 401; permission, MFA and credential-kind restrictions return 403. Request validation
returns 400, absent resources 404, stale versions or conflicting state 409, and rate limiting 429. Endpoint filters
and handlers determine the specific response: the declarations are not a complete list of possible errors.
Use [Errors and responses](../Architecture/Errors-and-Responses.md) for error codes and envelopes.

No general idempotency wrapper is declared on these routes. Refresh, OTP consumption, one-time SSO exchange and
key creation must not be blindly retried after an ambiguous response.

## Guardrails

`AdminEndpointAuthorizationTests` checks explicit authorization and registered permission policies.
`RoleHierarchyTests`, `PermissionPolicyTests`, `SessionManagementTests`, `ApiKeyAuthenticatorTests`, `ApiKeyHandlerTests` and `AdminAuditTests` exercise
handler restrictions. To re-audit, enumerate every `ICarterModule`, expand `MapGroup` prefixes and inspect helper
conventions (`Read`, `Write`, `Gate`) as well as chained endpoint metadata.

## Related

[Identity](README.md), [Permissions and 2FA](Permissions-2FA-and-Admin-Audit.md),
[Frontend integration](Frontend-Integration.md), [Passkeys](Passkeys.md), [Enterprise SSO](Enterprise-SSO.md),
[SCIM](SCIM.md), [ADR-0013](../Decisions/0013-enterprise-sso-and-scim.md); `/starterkit:add-feature`, `/starterkit:add-permission`.
