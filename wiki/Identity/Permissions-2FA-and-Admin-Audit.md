# Permissions, admin 2FA and the Identity admin audit

Admin governance in the Identity module: what each role may do, how admins prove a second factor, and how
privileged actions are recorded.

## 1. Roles and permissions

### Roles (seeded on startup by `IdentityDataSeeder`)

| Role | Id | Purpose |
|------|----|---------|
| User | `11111111-1111-1111-1111-111111111111` | every registered user |
| Admin | `33333333-3333-3333-3333-333333333333` | administrators |
| SuperAdmin | `44444444-4444-4444-4444-444444444444` | unrestricted |
| Finance | `66666666-6666-6666-6666-666666666666` | back-office finance staff |
| Support | `77777777-7777-7777-7777-777777777777` | back-office support staff |

Role names live in two places that must stay equal (a test enforces it): `Identity/Domains/Roles/Constants/RoleConstants.cs`
(names + ids) and `Shared.Core/Authorization/RoleConstants.cs` (names).

Granting or revoking a staff role (Admin, SuperAdmin, Finance, Support) is SuperAdmin-only. An Admin manages plain
users only, never staff.

### Permissions (`Shared.Core/Authorization/Permissions.cs`)

The role-to-permission mapping is defined once, in code: `Shared.Core/Authorization/RolePermissionMap.cs`.
Access tokens carry roles only; `PermissionClaimsTransformation` adds the `permission` claims on every request
(any identity provider, map changes apply to live tokens). Login/profile responses return `Permissions` for the UI.

| Permission | SuperAdmin | Admin | Finance | Support | User |
|------------|:--:|:--:|:--:|:--:|:--:|
| `billing.read` | x | x | x | x | |
| `billing.approve` | x | x | x | | |
| `billing.refund` | x | x | x | | |
| `billing.export` | x | x | x | | |
| `plans.manage` | x | x | x | | |
| `payment-settings.manage` | x | x | x | | |
| `billing.coupons.manage` | x | x | x | | |
| `users.read` | x | x | | x | |
| `users.manage` | x | x | | | |
| `admins.manage` | x | | | | |
| `audit.read` | x | x | x | | |
| `catalog.moderate` | x | x | | | |

- **Token:** at login/refresh, `JwtTokenGenerator` writes one de-duplicated `permission` claim for each permission
  the user's roles grant. A change to the map shows up at the next login or refresh (and immediately for
  permission checks, which `PermissionClaimsTransformation` derives per request).
- **Role changes apply immediately.** Every Identity role change ends all sessions of the target through
  `ISessionRevoker` (rotates the security stamp, revokes refresh tokens, revokes access tokens with the old stamp):
  `AssignRole`, `RevokeRole`, `PromoteToAdmin`, `DemoteAdmin`. A grant does not wait for the next refresh:
  live tokens are a cache of the old role set, and a new privileged session should start from a fresh login (with its
  2FA step). `CreateAdmin` creates a user with no sessions, so there is nothing to revoke. The caller can never be the
  target (`IRoleHierarchyGuard`), so an admin never signs themselves out this way.
- **Tenancy roles** (`Tenancy` module) are not in the token: `TenantMembershipResolver` loads the membership and its
  permissions per request (cached; membership changes drop the user's entry, role changes bump a per-tenant
  generation). Other instances can serve the old value for up to `MultiTenancy:Cache:LocalSeconds` (L1 memory cache).
- **Policies:** the Identity module registers one policy for each permission, named `perm:<permission>`
  (`IdentityAuthorizationExtensions`). Each policy requires an authenticated user, the permission claim, and the MFA
  rule (see section 2).
- **Endpoints:** use `.RequirePermission(Permissions.X)`. The `AdminOnly` and `SuperAdminOnly` policies still work,
  and they also carry the MFA rule.
- **Architecture test:** `Api.Tests/Architecture/AdminEndpointAuthorizationTests` fails when an endpoint under an
  `/admin` or `/superadmin` route lacks an admin-grade policy (`AdminOnly`, `SuperAdminOnly` or `perm:*`), or when an
  endpoint uses an unregistered `perm:*` policy.

Identity endpoints use these permissions:

| Endpoints | Permission |
|-----------|------------|
| `GET api/identity/users`, `GET api/identity/users/{id}` | `users.read` |
| edit, lock, unlock, status, assign role, revoke role | `users.manage` |
| `api/identity/admins/*`, promote, demote, `POST api/identity/users/{id}/2fa/reset` | `admins.manage` |
| `DELETE api/identity/users/{id}` | `SuperAdminOnly` |
| `GET api/v1/admin/identity/audit` | `audit.read` |

### How to add a permission

1. Add a `public const string` to `Permissions` and include it in `Permissions.All`. Values are stable strings:
   never rename one.
2. Grant it to roles in `RolePermissionMap`.
3. Protect the endpoints with `.RequirePermission(Permissions.YourPermission)`. The policy already exists because
   one is registered for each entry of `Permissions.All`.
4. Run `Identity.Tests` (`RolePermissionMapTests`, `PermissionPolicyTests`) and `Api.Tests`.

## 2. Admin two-factor authentication (TOTP, RFC 6238)

### Building blocks

- **TOTP:** ASP.NET Identity's built-in `AuthenticatorTokenProvider`; this module adds no custom TOTP code.
- **Authenticator key:** stored encrypted with ASP.NET Data Protection by `ProtectedUserStore` (purpose
  `Identity.TwoFactor.AuthenticatorKey.v1`).
- **Authenticator codes are single-use** (`ITotpReplayGuard`). The provider accepts a code for the current 30 s step
  and two steps either side (up to 150 s). After a code is ACCEPTED, the guard records `(user, SHA-256(user:code))`
  for `TotpReplayGuard.Window` (180 s). The same code is then refused for login verify, enable, disable and
  regenerate. On verify, disable and regenerate the refusal is a normal lockout strike.
  - Redis enabled: `SET key 1 PX 180000 NX`, atomic across instances. Redis disabled: the `IDistributedCache`
    fallback with a process-wide lock (exact for one instance, like the rest of the Redis-less mode).
  - Only a valid code is recorded, so a wrong guess never burns the next real code.
  - Fail closed: if Redis is unreachable the request fails (500, no strike) instead of silently skipping the check.
  - For clients and tests: two operations in the same 30 s step need two different codes (wait for the next code).
    Tests use `Totp.Code(key, stepsAhead)` with increasing offsets.
- **Recovery codes:** stored only as salted SHA-256 hashes. Each code works once, also under concurrency: redeeming
  rewrites the user row, and its concurrency stamp makes a parallel second redemption fail
  (`ReplayAndRoleChangeTests.Parallel_redemption_of_one_recovery_code_succeeds_exactly_once`).
- **Data Protection key ring:** persisted in `identity.data_protection_keys` (`DataProtectionKeyRepository`), so all
  instances share it and it survives redeploys. Losing the key ring makes every authenticator key unreadable.
  For at-rest encryption see the next section.

### Data Protection key ring (`Identity:DataProtection`)

Registered by `IdentityDataProtectionExtensions.AddIdentityDataProtection`.

| Key | Default | Meaning |
|-----|---------|---------|
| `ApplicationName` | `StarterKit` | `SetApplicationName`: the discriminator every instance shares. See the warning below. |
| `KeyEncryption` | inferred | `None` or `Certificate`. When unset: `Certificate` if a certificate is configured, otherwise `None`. |
| `RequireKeyEncryption` | `false` | Outside Development/Testing: `false` starts and logs a startup **warning** when keys are unencrypted; `true` refuses to start. |
| `Certificate:Thumbprint` | – | Certificate (with private key) in `Certificate:StoreLocation`/`StoreName` (default `CurrentUser`/`My`). Wins over `Path`. |
| `Certificate:Path` | – | PFX file, e.g. `/run/secrets/dataprotection_cert.pfx`. |
| `Certificate:Password` | – | PFX password. Docker secret `dataprotection_cert_password`. |
| `Certificate:PreviousPaths` | `[]` | Old PFX files, kept only to DECRYPT keys written before a rotation (same password). |

- **`ApplicationName` is permanent.** Payloads protected under another name (authenticator keys, open reset links)
  become unreadable. Before this setting existed, the discriminator was derived from the content root (`/app` in the
  container). An existing deployment with enrolled 2FA users must set `ApplicationName` to that old value, or have
  its users re-enroll.
- **Certificate mode** calls `ProtectKeysWithCertificate(current)` and
  `UnprotectKeysWithAnyCertificate(current, previous...)`. A PFX-loaded certificate is not in the machine store, so it
  must be registered for decryption explicitly. The certificate is loaded at registration: a missing file, a wrong
  password or a certificate without a private key stops startup.
- **Existing unencrypted keys** stay readable after encryption is turned on; only new keys are encrypted. The old
  keys age out with the key lifetime (90 days).
- **Why the default is warn, not fail.** The key ring lives in the same database as the data it protects, so the
  certificate protects against a database-only leak (dump, backup, read replica), not against a full host compromise.
  Failing by default would break every existing deployment and every fresh deploy that has no certificate yet. The
  production overlay `docker-compose.dataprotection.yml` mounts the PFX and sets `RequireKeyEncryption=true`, so a
  stack that opts in cannot silently fall back to unencrypted keys.
- **KMS:** add a value to `DataProtectionKeyEncryption` (e.g. `AzureKeyVault` via `ProtectKeysWithAzureKeyVault`, or
  AWS KMS via a custom `IXmlEncryptor`/`IXmlDecryptor`) and handle it in `ConfigureKeyEncryption`.
- **Rotation:** issue a new PFX, set it as `Certificate:Path`, add the old one to `Certificate:PreviousPaths`, and
  remove it after 90 days. `deployment/secrets-setup.sh` never overwrites an existing PFX.
- **Back up the PFX** with the database: a restored database cannot read any encrypted key without it.

### Endpoints for the signed-in user

| Endpoint | Body | Notes |
|----------|------|-------|
| `POST api/auth/2fa/setup` | – | Returns `sharedKey` and an `otpauth://` URI. The key is pending: 2FA stays off. |
| `POST api/auth/2fa/enable` | `{ code }` | Enables 2FA and returns the recovery codes (shown once). Revokes every session and returns a new MFA session. |
| `POST api/auth/2fa/disable` | `{ password, code }` | `code` can be an authenticator code or a recovery code. Removes the key and the codes, revokes every session, and returns a new non-MFA session. |
| `POST api/auth/2fa/recovery-codes/regenerate` | `{ code }` | Old codes stop working. |
| `POST api/auth/2fa/verify` | `{ challengeToken, code }` or `{ challengeToken, recoveryCode }` | Second login step. The anonymous `auth` rate limit applies. A code already accepted by any 2FA endpoint is refused. |
| `POST api/identity/users/{id}/2fa/reset` | `{ reason }` | SuperAdmin only (`admins.manage`). Removes the target's 2FA, ends all of their sessions, and is audited. |

### Login flow (client contract)

1. `POST api/auth/login` checks the password. If 2FA is off, the response is unchanged.
2. If 2FA is on, the response is `200` with `requiresTwoFactor: true`, `twoFactorChallengeToken` and
   `twoFactorChallengeExpiresAt`. `accessToken` and `refreshToken` are `null`.
   - The challenge token is an HS256 JWT signed with a key derived from the JWT secret, with audience
     `<audience>:2fa-challenge` and `token_use=2fa_challenge`. It is not an access token.
   - It expires after 5 minutes and is bound to the security stamp.
3. `POST api/auth/2fa/verify` returns the normal `LoginResult`.
   - The access token carries `amr: ["otp","mfa"]`.
   - The refresh-token row stores `multi_factor = true`, so refreshed tokens keep `amr=mfa`.
   - A self-service password change from an MFA session also keeps it.
4. Every wrong code adds a strike through Identity `AccessFailed`, and the account locks after
   `IdentitySettings:Lockout:MaxFailedAccessAttempts`.
   - Strikes are recorded outside the unit of work, so a rejected request cannot roll them back.
5. External login (Google/Facebook) is only the first factor. A user with 2FA enabled gets the same
   `requiresTwoFactor` + challenge response from the callback; `bypassTwoFactor` is gone.

### Enforcement

| Config key | Default | Meaning |
|------------|---------|---------|
| `Identity:TwoFactor:RequiredForRoles` | `["SuperAdmin","Admin","Finance"]` | Callers with any of these roles need an MFA session on `perm:*` / `AdminOnly` / `SuperAdminOnly` policies. `[]` turns the rule off. |
| `Identity:TwoFactor:EnforcementMode` | `Enforce`, except `Warn` in Development/Testing | `Enforce`: the request gets `403`. `Warn`: the request is allowed and logged, and the response gets an `X-Auth-Error: mfa_required` header. |
| `Identity:TwoFactor:ChallengeTokenLifetimeMinutes` | `5` | Clamped to the range 1–15. |
| `Identity:TwoFactor:RecoveryCodeCount` | `10` | Clamped to the range 5–20. |
| `Identity:TwoFactor:Issuer` | `StarterKit` | Label shown in authenticator apps. |

When the only reason for a 403 is the MFA rule, the response is:

```json
HTTP/1.1 403
X-Auth-Error: mfa_required
{ "isSuccess": false, "statusCode": 403, "errors": ["mfa_required", "Two-factor authentication is required ..."] }
```

A missing permission is always a plain 403, never `mfa_required`.

**Rollout:** deploy with `EnforcementMode=Warn`. Have each admin enroll, watch the warnings, then switch to
`Enforce`.

**E2E:** `TestAuthHandler` derives permission claims from `X-Test-Roles`. The `X-Test-Permissions` header overrides
them, and `X-Test-Mfa: true` adds `amr=mfa`. A real `Authorization: Bearer` token (without `X-Test-UserId`) is
forwarded to the real JWT bearer scheme.

## 3. Identity admin audit (`identity.admin_action_audit_logs`)

- **Shape:** admin id, action, target type and id, reason, `before_json` / `after_json` (jsonb), IP, user agent,
  correlation id, `occurred_at`.
  - The table is append-only: `ApplicationIdentityDbContext` refuses to update or delete a row.
- **Never stored:** passwords, hashes, security stamps, keys, codes or tokens.
  - The before/after JSON comes only from `UserAuditSnapshot`, and a test rejects credential-like fields in it.
- **Actions:**
  - `user.edited`, `user.locked`, `user.unlocked`, `user.status_changed`, `user.deleted`
  - `role.assigned`, `role.revoked`
  - `admin.promoted`, `admin.demoted`, `admin.created`, `admin.updated`, `admin.password_changed`
  - `2fa.enabled`, `2fa.disabled`, `2fa.reset`, `2fa.recovery_codes_regenerated`, `2fa.recovery_code_used`
- **Same unit of work:**
  - Commands that implement `Identity.Contracts.Audit.IAuditedCommand` run inside `IIdentityUnitOfWork` through the
    `AuditedCommandTransactionBehavior`. The transaction covers every UserManager save and the audit row.
  - Handlers record with `IIdentityAuditWriter.Record(...)`.
  - The 2FA disable, regenerate and verify handlers check credentials first, then run `IIdentityUnitOfWork` explicitly.
    This keeps lockout strikes from being rolled back.
- **Read:** `GET api/v1/admin/identity/audit` requires `audit.read`. It is paged and newest first, with filters
  `adminId`, `action`, `targetType`, `targetId`, `from`, `to`.

To audit a new admin command:

1. Implement `IAuditedCommand`.
2. Inject `IIdentityAuditWriter` and call `Record(action, targetId, reason, before: UserAuditSnapshot.Of(...), after: ...)`.
3. Add the action constant to `IdentityAuditActions`.
