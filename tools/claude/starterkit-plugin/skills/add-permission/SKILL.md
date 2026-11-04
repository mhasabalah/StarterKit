---
name: add-permission
description: Add and enforce authorization in StarterKit - a new platform permission (Permissions + RolePermissionMap) or tenant permission (TenantPermissions + SystemTenantRoles), and protect endpoints with RequirePermission / RequireTenantPermission / RequireTenantMember / HostOnly / DisallowImpersonation. Use for "restrict endpoint", "only admins can", "add a permission", "role-based access", "tenant role can".
---

# Add a permission / protect an endpoint

StarterKit has **two** permission planes. Pick the right one first.

| Plane | Who holds it | Where it comes from | Endpoint call |
|---|---|---|---|
| **Platform** (`Shared.Core/Authorization/Permissions.cs`) | Host staff: SuperAdmin, Admin, Finance, Support | The JWT carries **roles only**. `PermissionClaimsTransformation` derives `permission` claims per request from `RolePermissionMap` | `.RequirePermission(Permissions.X)` |
| **Tenant** (`Shared.Core/MultiTenancy/Authorization/TenantPermissions.cs`) | Members of the *current* tenant | The member's tenant roles, read from the membership DB, not the JWT | `.RequireTenantPermission(TenantPermissions.X)` or `.RequireTenantMember()` |

The two cases that cause confusion:
- Platform admins administering tenants use `.HostOnly().RequirePermission(Permissions.TenantsManage)`.
- In single-tenant mode, platform Admins also satisfy tenant permissions; in multi-tenant mode only memberships do.

## Steps: platform permission

1. Add a `const string` to `Permissions.cs` named `<Area><Verb>`, with a dotted value: `"inventory.manage"`.
   Values are stable; never rename one (they live in tokens, caches and client code).
2. Add it to `Permissions.All`, or no policy gets registered and the endpoint 500s.
3. Grant it in `RolePermissionMap.cs`. SuperAdmin and Admin get `All` automatically; add it explicitly to Finance or
   Support if needed. Keep `RoleConstants` in Identity and in `Shared.Core.Authorization` in sync if you add a
   **role**.
4. Use `.RequirePermission(Permissions.InventoryManage)` on the endpoint. Admin routes (`/admin`, `/superadmin`)
   **must** carry `AdminOnly`, `SuperAdminOnly` or a `perm:*` policy; `AdminEndpointAuthorizationTests` fails
   otherwise.
5. Privileged roles need 2FA. See `wiki/Identity/Permissions-2FA-and-Admin-Audit.md`. Admin mutations should be
   audited.

## Steps: tenant permission

1. Add a `const string` to `TenantPermissions.cs` under `// Business modules`, with a value of
   `"<module>.<resource>.<verb>"` (`"inventory.stock.manage"`).
2. Add it to `TenantPermissions.All`.
3. Grant it in the default tenant roles in
   `src/Modules/Tenancy/Tenancy/Domains/Roles/Models/TenantRole.cs` → `SystemTenantRoles`. `Admin` uses
   `TenantPermissions.All`; add it to `BranchManager` or `Member` as the domain requires. Existing tenants' system
   roles: check how Tenancy re-syncs role definitions on startup, and say so in your summary if it needs a manual step.
4. Use `.RequireTenantPermission(TenantPermissions.X)` on the endpoint. Data endpoints of tenant-data modules already
   require a resolved tenant.
5. For row-level ownership ("only the creator may edit"), add a domain service such as
   `Catalog/Domains/Products/Services/ProductAuthorizationService.cs`. Permissions decide *which actions*; services
   decide *which rows*.

## Endpoint auth cheat-sheet

Every endpoint must declare one of these (architecture test). Undeclared Carter endpoints fall back to "authenticated
user" at runtime, which is a safety net, not a choice.

```csharp
.RequireAuthorization()                                   // any signed-in user
.RequireAuthorization("PageGate")                         // page 1 public, page 2+ signed in
.RequireAuthorization(PolicyConstants.AdminOnly)          // legacy role gate - prefer permissions
.RequirePermission(Permissions.UsersRead)                 // platform staff
.RequireTenantMember()                                    // any member of current tenant
.RequireTenantPermission(TenantPermissions.BranchesManage)
.HostOnly().RequirePermission(Permissions.TenantsManage)  // platform over tenants
.DisallowImpersonation()                                  // destructive / owner actions (impersonation tokens carry `act`)
.AllowAnonymous()                                         // explicit public
```

References: `Identity/Domains/Users/Features/GetUsers/GetUsersEndPoint.cs` (platform) and
`Tenancy/Domains/Branches/Features/BranchesModule.cs` (tenant).

## Tests

- E2E: `src/Api.E2ETests` with `fixture.CreateClientAs(userId, RoleConstants.X)`. Assert 403 for a role without the
  permission and 200 for one with it. For tenant permissions, use `MultiTenantE2EFixture` with two members.
- `RolePermissionMap` unit test: the role → permission matrix you intend.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Api.Tests --filter "FullyQualifiedName~AdminEndpointAuthorization"
dotnet test src/Api.E2ETests --filter "FullyQualifiedName~<Feature>"     # Docker
```
