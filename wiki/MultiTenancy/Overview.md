# Multi-Tenancy - Overview

StarterKit can run **single-tenant** (default) or as a **multi-tenant SaaS** where many companies (tenants) share one
deployment. Each company can have a **branch tree** (HQ → region → branch), **members** with tenant roles, and its
data isolated either in the **shared database** or in its **own dedicated database**.

| Document | What it covers |
|---|---|
| **Overview** (this page) | Requirements, architecture, request flow, NFRs, threat model |
| [Isolation-and-Dedicated-Databases.md](Isolation-and-Dedicated-Databases.md) | The 3 isolation layers, shared vs dedicated, RLS, connection resolution |
| [Branches-and-Scoping.md](Branches-and-Scoping.md) | Branch tree, branch assignments, branch-scoped data |
| [Adding-a-Tenant-Aware-Module.md](Adding-a-Tenant-Aware-Module.md) | Checklist for new modules/entities/jobs/caches |
| [Operations.md](Operations.md) | Enabling, provisioning, migrations, suspension, deletion/purge, runbooks |

---

## 1. Switching it on

```jsonc
"MultiTenancy": {
  "Enabled": true,                       // false = single tenant (the Default tenant everywhere)
  "DefaultIsolation": "Shared",          // Shared | Dedicated for new tenants
  "Resolution": { "RootDomain": "api.example.com" }  // acme.api.example.com -> tenant "acme"
}
```

**Off means identical to before.** With `Enabled=false` every row belongs to the always-present **Default tenant**
(`00000000-0000-0000-0000-000000000001`). The schema is the same in both modes, so switching on later needs **no data
migration**: existing data simply is the Default tenant's data. Branches, members and tenant roles work in both modes
(a single company can still have branches).

---

## 2. Functional requirements (what the platform offers)

| Area | Capability | Endpoint(s) |
|---|---|---|
| Sign-up | Any user creates a company and becomes its **owner** (shared isolation) | `POST /api/v1/tenants` |
| Tenant switcher | Global users belong to **many tenants**; list and switch | `GET /api/v1/me/tenants`, `POST /api/auth/switch-tenant` |
| Tenant profile | Name, legal name, locale, currency, branding | `GET/PUT /api/v1/tenants/current` |
| Custom domains | Add, verify (DNS TXT), remove | `/api/v1/tenants/current/domains` |
| Ownership | Transfer ownership; request deletion (grace period) | `/api/v1/tenants/current/transfer-ownership`, `/deletion` |
| Branches | Tree CRUD, move subtree, (de)activate | `/api/v1/branches` |
| Members | List, update roles + branches, suspend, remove, leave | `/api/v1/members` |
| Invitations | Invite by e-mail (link e-mailed, seat-limited by plan), re-send, revoke, accept | `/api/v1/tenant-invitations` |
| Tenant roles | System roles + custom roles from a permission catalog | `/api/v1/tenant-roles`, `/api/v1/tenant-permissions` |
| Audit | Append-only tenant audit trail | `GET /api/v1/tenant-audit` |
| Data export | Owner downloads all tenant data (JSON + CSV per table) | `POST /api/v1/tenants/current/export` |
| Billing | The **tenant** is the subscriber; expiry suspends, activation reactivates | Subscriptions endpoints |
| Platform admin | List, suspend, reactivate, delete, cancel deletion, **move data** (shared ⇄ dedicated, automated), re-provision | `/api/v1/host/tenants` |
| Support | Audited, short-lived, restricted impersonation of a tenant member | `POST /api/identity/impersonations` |

Global users + memberships: one login (e-mail) can be a member of several tenants with different roles in each - the
Slack/Notion/Azure model. Identity stays global; the **Tenancy** module owns tenants, branches, memberships, roles,
invitations and the audit trail.

---

## 3. Architecture

```
                         ┌──────────────── CONTROL PLANE (host database) ────────────────┐
 request ──► Auth ──► TenantResolutionMiddleware ──► Authorization ──► endpoint
                │        │  claim tid / host / X-Tenant                   │
                │        │  ITenantStore (L1+L2 cache)                    │  tperm:* policies
                │        │  ITenantMembershipResolver (L1+L2 cache)      │  (membership permissions)
                │        ▼                                                ▼
                │   TenantScope (AsyncLocal) ───────────────► ITenantContext
                │                                                         │
                │        identity.*  tenancy.*  subscriptions.*  hangfire │ (always host DB)
                └─────────────────────────────────────────────────────────┘
                                         │
                ┌──────────── DATA PLANE (shared DB or tenant DB) ───────────────┐
                │  ITenantDataContext: CatalogDbContext, AttachmentDbContext, …  │
                │  connection = ITenantConnectionStringResolver(tenant)          │
                │  EF named filters: Tenant ∧ Branch ∧ SoftDelete ∧ …            │
                │  TenantEntityInterceptor (stamp / block cross-tenant writes)   │
                │  TenantSessionConnectionInterceptor → Postgres RLS             │
                └────────────────────────────────────────────────────────────────┘
```

### Building blocks (`src/Shared/Shared.Core/MultiTenancy`)

| Type | Role |
|---|---|
| `MultiTenancyOptions` | `MultiTenancy` configuration section |
| `TenantInfo`, `TenantStatus`, `TenantIsolation` | Cache-friendly tenant snapshot |
| `TenantScope` / `ITenantContext` | AsyncLocal ambient tenant (flows into handlers, events, jobs, consumers) |
| `ITenantStore` / `ITenantMembershipResolver` | Lookups (implemented + cached by the Tenancy module) |
| `IMustHaveTenant`, `IBranchScoped`, `ITenantDataContext`, `[HostEntity]` | Markers |
| `AppDbContextBase.ApplyTenantFilters` | Named `Tenant` / `Branch` filters + `tenant_id` indexes |
| `TenantEntityInterceptor` | Write-side isolation |
| `TenantSessionConnectionInterceptor` + `TenantRlsMigrationExtensions` | Postgres RLS |
| `TenantConnectionStringResolver` | Shared vs dedicated database |
| `TenantResolutionMiddleware` + `RequireTenant()/HostOnly()/TenantOptional()` | Request resolution |
| `TenantPermissions` + `RequireTenantPermission()/RequireTenantMember()` | Tenant authorization |
| `ITenantJobRunner` | Per-tenant fan-out for background work |
| `ITenantCacheKeys` | `t:{tenantId}:` cache keys |
| `TenantDataPurger`, `TenantDataPurgeRequestedEvent` | Tenant erasure |
| Hangfire `TenantJobFilter`, MassTransit `Tenant*Filter` | Tenant propagation across async boundaries |

### Request flow

1. **Authentication** (JWT) - the token may carry `tid` (tenant) and `tms` (membership stamp).
2. **Tenant resolution** (`UseMultiTenancy`, between authentication and authorization):
   - **Token claim `tid`** is authoritative. A host/header naming another tenant → **403 `tenant_mismatch`**.
   - Otherwise **host** (`{identifier}.{RootDomain}` or a verified custom domain), then the **`X-Tenant`** header
     (id or identifier) - for login pages, public storefronts and service calls.
   - Tenant must exist (404) and be serviceable: `Suspended` → 403 `tenant_suspended` (or GET-only in
     `SuspendedTenantMode=ReadOnly`), `Provisioning/PendingDeletion` → 403 `tenant_unavailable`.
   - A token's tenant must still list the user as an active member (403 `tenant_access_denied`) with the same
     membership stamp (401 `tenant_membership_changed` → refresh the token).
   - Optional `X-Branch` selects the active branch (must be inside the user's branch scope).
3. **Endpoint requirement**: endpoints in modules that own an `ITenantDataContext` **require** a tenant by default
   (400 `tenant_required`); others are optional (an invalid tenant is ignored, not fatal); `HostOnly()` endpoints run
   tenant-less (platform/control plane).
4. **Authorization**: `tperm:*` policies check the **membership's** permissions (owner = all). Tenant permissions are
   **not** in the JWT - they are resolved per request from the cached membership, so role changes apply immediately.
5. **Data access**: filters, interceptor and RLS all read the ambient tenant.

### Sessions

* Login picks the tenant addressed by the request (host/header), else the user's last active tenant, else their only
  tenant; otherwise the session is tenant-less and the client calls `POST /api/auth/switch-tenant`.
* Refresh tokens remember their tenant and re-validate the membership on every rotation (removed member → the
  session continues tenant-less).
* `GET /api/v1/members/me` returns the caller's effective permissions and branch scope (for UI gating).

---

## 4. Non-functional requirements

| NFR | Target / mechanism |
|---|---|
| **Isolation** | Three independent layers (resolution validation, EF named filters + write interceptor, Postgres RLS). Fail closed: no tenant → no rows, no writes. Cross-tenant access answers **404** (no existence oracle). Enforced by architecture tests. |
| **Performance** | Tenant + membership resolution: one cached lookup each per request (L1 memory 30 s, L2 Redis 10 min; misses cached 30 s). `tenant_id`-leading indexes on every tenant table; unique indexes are per tenant. Branch subtree = one indexed prefix query (materialized path, `text_pattern_ops`). |
| **Scalability** | Shared DB scales to thousands of tenants (row-level). Dedicated DBs are bounded by `MaxDedicatedPoolSize` per tenant pool; the tenant → connection mapping is the hook for sharding (several servers). |
| **Availability** | Background work fans out per tenant; one tenant's failure (job, migration, purge) is logged and skipped. Dedicated DB migrations run in the background at startup with bounded parallelism. |
| **Security** | No spoofable tenant header (validated against the token); privilege-escalation guard (members can only grant what they hold); host endpoints require platform permissions (+ admin 2FA rules); dedicated connection strings live in configuration/Docker secrets, never in the DB; append-only tenant audit trail. |
| **Noisy neighbour** | Optional per-tenant global rate limit (`RateLimiting:Tenant`), plan seat limits (`MaxUsers` on invitations). |
| **Observability** | `TenantId`/`TenantIdentifier` in the logging scope of every request and job; `tenant.id` activity tag on traces. No tenant label on metrics (bounded cardinality). |
| **Compliance** | Region field (data residency routing hook), deletion with grace period + irreversible purge (rows, dedicated DB, blobs), tenant audit trail. |
| **Backward compatibility** | `Enabled=false` behaves like the original single-tenant template; enabling later needs no data migration. |
| **Testability** | In-memory isolation tests, Testcontainers RLS tests, E2E multi-tenant collection, architecture guardrails. |

---

## 5. Threat model (abridged)

| Threat | Mitigation |
|---|---|
| Client sends another tenant's id in `X-Tenant` | Token claim wins; mismatch → 403. Anonymous callers only get public data (no membership → no tenant permissions). |
| Forged id in a request body (IDOR across tenants) | Tenant filter hides the row (404); attach-and-update is blocked by `TenantEntityInterceptor` (logged as a security event). |
| Developer forgets the filter / uses `IgnoreQueryFilters()` | Named filters; architecture test forbids the parameterless call; RLS still blocks at the database. |
| Raw SQL | RLS (non-superuser role) - see [Isolation](Isolation-and-Dedicated-Databases.md#row-level-security). |
| Background job runs without a tenant | Fail closed (no rows); recurring jobs fan out per tenant; Hangfire/MassTransit carry the tenant. |
| Cache poisoning across tenants | `ITenantCacheKeys` (`t:{tenantId}:` prefix); throws without a tenant. |
| Removed member keeps a valid JWT | Middleware re-checks the (cached) membership on every request + stamp check; refresh drops the tenant. |
| Tenant admin escalates privileges | Can only grant roles/permissions/branches it holds itself; owner-only ownership/deletion. |
| Orphan-blob sweeper deletes another tenant's files | Storage keys are tenant-prefixed (`t/{tenantId}/`); the sweeper lists only the current tenant's prefix. |
| Support staff abuses impersonation | Permission + 2FA, mandatory reason, ≤ 60 min access-only token, staff accounts not impersonable, no credential/session/owner actions, audited in BOTH the admin audit and the tenant's own trail. |
| Malicious site calls the API from a browser | CORS allows only configured origins, tenant subdomains of existing tenants and VERIFIED custom domains. |
| Writes lost during a tenant move | `Migrating` blocks HTTP writes (503) and defers jobs/messages; copy verified by row counts in one transaction before the switch. |
