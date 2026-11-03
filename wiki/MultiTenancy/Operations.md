# Multi-Tenancy Operations

## Enabling multi-tenancy on an existing deployment

1. Apply the migrations (startup does it): `AddMultiTenancy` (Catalog, Attachment), `AddMultiTenancySessions`
   (Identity), `InitialTenancy` (Tenancy). Existing rows become the **Default tenant**'s.
2. Production DB role: switch the app to a **non-superuser** role so RLS is effective
   ([Isolation](Isolation-and-Dedicated-Databases.md#row-level-security)).
3. Set `MultiTenancy__Enabled=true` (+ `MULTITENANCY_ROOT_DOMAIN` for subdomains) and redeploy.
4. Existing users have no memberships yet: make the original operator the Default tenant's owner (SQL below) or have
   users create their own tenants.

```sql
-- make a user the owner of the Default tenant (run once, as a superuser/host connection)
INSERT INTO tenancy.tenant_memberships (id, tenant_id, user_id, status, is_owner, role_ids, stamp, joined_at,
                                        branches, is_deleted, version)
-- role 60000000-…-0001 = the Default tenant's system "Administrator" role
VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', '<USER_ID>', 'Active', true,
        '{60000000-0000-0000-0000-000000000001}',
        md5(random()::text), now(), '[]', false, 0);
UPDATE tenancy.tenants SET owner_user_id = '<USER_ID>' WHERE id = '00000000-0000-0000-0000-000000000001';
```

## Subdomains and custom domains (Nginx / TLS)

* DNS: `*.api.example.com` → the server; certificate for `api.example.com` + `*.api.example.com` (wildcard needs the
  ACME **DNS-01** challenge).
* Nginx: `server_name api.example.com *.api.example.com;` and forward the host (`proxy_set_header Host $host;`) - see
  the comment in `deployment/nginx/conf.d/app-production.conf`.
* Custom domains: the tenant adds `shop.acme.com`, creates the TXT record `_tenant-verify.shop.acme.com = <token>`,
  calls `POST /tenants/current/domains/{host}/verify`. Route the domain to the API (CNAME) and issue its certificate
  (e.g. Caddy/Traefik on-demand TLS, or a cert-manager per domain).
* CORS: besides `Cors:AllowedOrigins`, tenant frontends are allowed automatically (`TenantCorsPolicyProvider`):
  `https://{identifier}.{MultiTenancy:Cors:FrontendRootDomain}` of existing tenants and `https://{host}` of VERIFIED
  custom domains (plain `http` only in Development). The configured policy (credentials, methods, headers) applies.

## Tenant lifecycle

| Status | Meaning | Who |
|---|---|---|
| `Provisioning` | Dedicated DB being created/migrated | system |
| `ProvisioningFailed` | See `status_reason`; retry with `POST /host/tenants/{id}/provision` | platform |
| `Active` | Serving | - |
| `Suspended` | Blocked (or read-only, `SuspendedTenantMode`) - manual or **subscription expired** (auto-lifted on activation) | platform / billing |
| `Migrating` | Data being moved between databases: reads work, writes get 503 `tenant_migrating`, jobs/messages are deferred | platform |
| `PendingDeletion` | Owner/platform requested deletion; unavailable; purge after `DeletionGracePeriodDays` | owner / platform |
| `Deleted` | Data purged; tombstone row keeps the identifier reserved | system |

`POST /host/tenants/{id}/cancel-deletion` returns a pending tenant to `Suspended` (reactivate after review).

## Purge (daily `tenancy-purge-deleted-tenants`, 03:00)

For each tenant past its grace period: modules erase external data (`TenantDataPurgeRequestedEvent` → blobs under
`t/{id}/`), then the dedicated database we created is **dropped** (otherwise the tenant's rows are deleted from every
tenant-data context), then memberships/roles/invitations/branches/audit are removed and the tenant becomes `Deleted`.
A failure is retried on the next run. **Irreversible** - take backups per your retention policy.

## Moving a tenant between shared and dedicated (automated)

`POST /api/v1/host/tenants/{id}/move` `{ "isolation": "Dedicated", "connectionSecretRef": null }` (permission
`tenants.manage`) → 202. Shared ⇄ dedicated, or dedicated → another server (`connectionSecretRef`).

| Step | What happens |
|---|---|
| Request | Target validated (connection resolvable, NOT the database the tenant lives in); tenant → `Migrating` (read-only); `MoveTenantJob` queued; audit `tenant.move-requested` |
| Propagation | Waits one L1 cache period (`Cache:LocalSeconds` + 5 s) so every instance sees `Migrating`: HTTP writes → 503, Hangfire jobs of the tenant retry later, MassTransit messages are retried with back-off |
| Target | Dedicated: database created + migrated (no seeding) |
| Copy | Per tenant-data context ONE target transaction: leftovers of a failed attempt deleted, every tenant table streamed with binary `COPY` from a `REPEATABLE READ` snapshot, per-table row counts verified (`TenantDataMover`) |
| Switch | Tenant points at the target, previous status restored, caches dropped; audit `tenant.move-completed` (row counts) |
| Cleanup | After another cache period: a dedicated database we created is DROPPED, otherwise the tenant's rows are purged from the source |

* **Failure before the switch**: the tenant stays where it was (status restored, audit `tenant.move-failed` with the
  error) and the target's partial copy is removed. Fix the cause and request the move again.
* **Crash / stuck `Migrating`**: `POST /host/tenants/{id}/move/retry` (idempotent) or `/move/abort` (stays put).
* **Cleanup failure** (after a successful switch) is logged (`Tenant move: cleaning the source database failed`);
  the tenant already works from the target - remove its rows from the source by hand (they are isolated anyway).
* Moves run one at a time (`DisableConcurrentExecution`). Read-only time ≈ 2 × cache period + copy time.
* Blobs do not move (storage is not per database). Billing suspensions arriving during a move apply when it ends.
* Manual fallback (metadata only): suspend, copy with `COPY … WHERE tenant_id = '<id>'`, then
  `PUT /host/tenants/{id}/isolation`, verify, delete the source rows, reactivate.

## Tenant data export (portability)

`POST /api/v1/tenants/current/export` - **owner only**, audited (`tenant.data-exported`), not available while
impersonating. Returns a zip: `manifest.json`, `tenant/*.json` (profile, branches, roles, members with e-mails,
invitations, audit trail) and `data/{schema}.{table}.csv` for EVERY table of every tenant-data context (shared or
dedicated database; read in the tenant's scope, so EF filters and RLS apply). Built in a temp file that is deleted
when the response ends. Files/blobs are listed in the attachment tables, not embedded.

## Invitations

`POST /tenant-invitations` e-mails the link (template `TenantInvitation`, culture from the tenant) built from
`MultiTenancy:Invitations:AcceptUrl` (`{token}` placeholder; relative → `Email:AppBaseUrl`). The token is returned in
the response only when `ReturnTokenInResponse=true` (apps delivering invitations themselves, tests). A failed e-mail
does not fail the request (`emailQueued=false`); `POST /tenant-invitations/{id}/resend` issues a NEW link (the old one
stops working) and restarts the lifetime. Accepting still requires a signed-in user with the same VERIFIED e-mail.

## Support impersonation

`POST /api/identity/impersonations` `{ tenantId, userId, reason, minutes? }` - permission `tenants.impersonate`
(SuperAdmin, Support; admin 2FA rules apply).

* Only active, non-staff users (no platform role besides User) who are active members of the tenant.
* Access token only (no refresh token), lifetime ≤ `MultiTenancy:Impersonation:MaxMinutes`, claim `act` = the
  platform user. The token carries the TARGET's membership - nothing more.
* Blocked while impersonating (403 `impersonation_restricted`): every write to Identity (password, 2FA, profile,
  sessions), every write to host endpoints (switch tenant, create tenant, accept invitation), and endpoints marked
  `.DisallowImpersonation()` (tenant deletion, ownership transfer, data export, leave tenant).
* Audited twice: Identity admin audit (`impersonation.started` + reason) and the TENANT's audit trail
  (`platform.impersonation-started`); every tenant audit entry made during the session records `ImpersonatorUserId`,
  every log line carries `ImpersonatorId`. Turn the feature off with `MultiTenancy:Impersonation:Enabled=false`.

## Notification logs

* `notification.notification_log` (every channel, e-mail included) carries `tenant_id` (stamped from the ambient tenant) and are
  erased by the tenant purge.
* Billing notifications are addressed to the subscriber (= the tenant): `INotificationRecipientResolver` delivers them
  to the tenant's OWNER.

## Observability

* Every log line of a tenant request/job carries `TenantId` and `TenantIdentifier` (Seq: `TenantId = '…'`).
* Traces: `tenant.id`, `tenant.identifier` tags.
* Security events: `SECURITY: blocked cross-tenant …` warnings from `TenantEntityInterceptor`; tenant mismatches are
  logged by the resolution middleware.

## Configuration reference

| Key | Default | Notes |
|---|---|---|
| `MultiTenancy:Enabled` | `false` | Master switch |
| `MultiTenancy:DefaultIsolation` | `Shared` | For new tenants |
| `MultiTenancy:SuspendedTenantMode` | `Block` | `ReadOnly` = GET still works |
| `MultiTenancy:AllowSelfServiceSignup` | `true` | `false` = only platform admins create tenants |
| `MultiTenancy:MaxBranchDepth` | `6` | 1-32 |
| `MultiTenancy:DeletionGracePeriodDays` | `30` | |
| `MultiTenancy:Resolution:RootDomain` | empty | Subdomain resolution |
| `MultiTenancy:Resolution:HeaderName` / `BranchHeaderName` | `X-Tenant` / `X-Branch` | |
| `MultiTenancy:Database:EnableRowLevelSecurity` | `true` | |
| `MultiTenancy:Database:DedicatedDatabaseNameFormat` | `{database}_t_{identifier}` | |
| `MultiTenancy:Database:MaxDedicatedPoolSize` | `20` | |
| `MultiTenancy:Database:MigrationParallelism` | `4` | |
| `MultiTenancy:Database:MigrateDedicatedOnStartup` | `true` | |
| `MultiTenancy:Connections:{ref}` | - | Dedicated connection strings (secrets!) |
| `MultiTenancy:Cache:LocalSeconds` / `DistributedSeconds` | `30` / `600` | `0` disables a level (also the move's propagation wait) |
| `MultiTenancy:Invitations:SendEmail` / `AcceptUrl` / `ReturnTokenInResponse` | `true` / `/invitations/accept?token={token}` / `false` | |
| `MultiTenancy:Cors:Enabled` / `FrontendRootDomain` / `AllowVerifiedCustomDomains` | `true` / empty / `true` | Tenant-aware CORS |
| `MultiTenancy:Impersonation:Enabled` / `MaxMinutes` | `true` / `30` | 5-60 |
| `MultiTenancy:DomainVerification:DohEndpoint` | Cloudflare DoH | |
| `RateLimiting:Tenant:PermitLimit` / `WindowSeconds` | `0` (off) / `60` | Per-tenant global budget |
