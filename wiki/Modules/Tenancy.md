# Tenancy module

`src/Modules/Tenancy` is the **control plane** of multi-tenancy. It owns tenants (companies), branch trees,
memberships, tenant roles, invitations, custom domains and the tenant audit trail. It runs both when
`MultiTenancy:Enabled=false` (everything lives in the Default tenant) and when it is `true`.

Concepts, isolation and operations are documented in the MultiTenancy section, which you should read first:

| Page | Covers |
|---|---|
| [Overview](../MultiTenancy/Overview.md) | Model, resolution, single- vs multi-tenant, configuration |
| [Branches and scoping](../MultiTenancy/Branches-and-Scoping.md) | Branch trees, `IBranchScoped`, branch permissions |
| [Isolation and dedicated databases](../MultiTenancy/Isolation-and-Dedicated-Databases.md) | Filters + RLS, shared vs dedicated DB, moves |
| [Operations](../MultiTenancy/Operations.md) | Provisioning, suspension, export, deletion and purge, moves |
| [Adding a tenant-aware module](../MultiTenancy/Adding-a-Tenant-Aware-Module.md) | Checklist for business modules |

## Shape

| | |
|---|---|
| Schema / context | `tenancy` / `TenancyDbContext` (host context, not tenant data) |
| Domains | `Tenants`, `Branches`, `Members`, `Roles`, `Invitations`, `Audit` (`Tenancy/Domains/*`) |
| Contracts | `Tenancy.Contracts`, the only Contracts project allowed to reference `Shared.Core` (whitelisted) |
| Consumes | `SubscriptionActivated/ExpiredIntegrationEvent` → `TenantBillingStatusConsumer` (suspend or reactivate on billing) |
| Handles in-process | `ImpersonationStartedEvent` → tenant audit (must succeed before the token is issued) |
| Jobs | `PurgeDeletedTenantsJob`: purges tenants past their deletion grace period through `TenantDataPurgeRequestedEvent` (blobs, logs) and generic purges of every `IMustHaveTenant` table |
| Startup | `UseTenancyModule()` runs **before** the other `Use*Module()` calls: the Default tenant must exist before tenant-data seeding |

## HTTP surface (`api/v1/...`)

The groups below are defined in `Tenancy/Domains/*/Features/*Module.cs`. For exact shapes and the permission on each
route, see `/swagger`.

| Group | Who | Examples |
|---|---|---|
| `tenants`, `me`, `me/tenants` | Signed-in users | Create a tenant (become owner), list my memberships |
| `tenants/current/...` | Members of the current tenant (`RequireTenantPermission`) | Settings, `domains` (+ verify), `export`, `deletion` / `cancel-deletion`, `transfer-ownership`, `leave` |
| `branches` | `tenant.branches.read` / `.manage` | Branch tree CRUD, activate or deactivate |
| `members` | `tenant.members.read` / `.manage` | Update roles and branches, suspend, reactivate, remove |
| `tenant-roles`, `tenant-permissions` | `tenant.roles.manage` | Custom tenant roles from `TenantPermissions` |
| `tenant-invitations` | `tenant.members.manage`; accept = invitee | Invite, resend, revoke, `accept` |
| `tenant-audit` | `tenant.audit.read` | Tenant audit trail |
| `host/tenants` | Platform staff, `.HostOnly()` + `tenants.read` / `tenants.manage` | List, suspend, reactivate, `provision`, `isolation` (shared or dedicated DB), `move` / `retry` / `abort` |

Destructive owner actions (deletion, ownership transfer) also call `.DisallowImpersonation()`.

## Tests

- `Tenancy.Tests`: domain rules and handler logic (EF InMemory).
- `Tenancy.IntegrationTests`: real Postgres + real migrations + the real `AddTenancyModule`. Covers atomic tenant
  creation (system roles, head office, owner membership, audit), provisioning enqueued after commit, idempotent
  seeding, and every database-enforced uniqueness rule the handlers translate by constraint name (identifier, custom
  domain, branch code per tenant, role name per tenant, one membership per user and tenant).
- HTTP flows across tenants: `src/Api.E2ETests/MultiTenancy`.

## Extending

- **New tenant permission:** `/starterkit:add-permission`. Add it to `TenantPermissions` and `All`, and grant it in
  `SystemTenantRoles`.
- **Business data per tenant:** keep it in your own module's `ITenantDataContext`, never in Tenancy.
- **Reacting to tenant lifecycle:** consume Tenancy's events through `.Contracts`, or add an integration event with
  `/starterkit:add-integration-event`.
