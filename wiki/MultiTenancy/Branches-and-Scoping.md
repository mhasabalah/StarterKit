# Branches and Branch Scoping

## The tree

Each tenant has a tree of branches (`tenancy.branches`): head office (created with the tenant) → regions → branches,
up to `MultiTenancy:MaxBranchDepth` levels (default 6).

* **Materialized path** - `path = /{rootId}/{childId}/…/{id}/` (32-char hex ids). "Branch + all descendants" is one
  indexed prefix query: `path LIKE '/root/cairo/%'` (index `(tenant_id, path text_pattern_ops)`).
* **Move** (`POST /branches/{id}/move`) re-parents a branch and rewrites its whole subtree's paths/depths in one
  set-based `UPDATE` inside a transaction; cycles and depth overflow are rejected.
* Branches are **deactivated, never deleted** (business rows keep pointing at them); children must be inactive first;
  the head office can't be deactivated.
* `Code` is unique per tenant (`HQ`, `CAI-01`…).

## Assignments and scope

A membership has `branches: [{ branchId, includeDescendants }]`. The membership resolver expands it into the
**effective branch scope** (`BranchScope.AllowedBranchIds`), cached with the membership:

| Member | Scope |
|---|---|
| Owner, or role with `tenant.branches.all` (e.g. Administrator) | Unrestricted (`AllowedBranchIds = null`) |
| Assigned to Cairo with descendants | Cairo + every branch under it |
| Assigned to Cairo without descendants | Cairo only |
| No assignments | Only tenant-wide data (`BranchId = null`) - secure default |

Creating/moving branches invalidates every membership cache of the tenant (scopes may have changed).

`X-Branch: {id}` optionally selects the **active branch** of a request (must be inside the scope) -
`ITenantContext.Branches.ActiveBranchId`, e.g. as the default branch of new records.

## Branch-scoped data

Implement `IBranchScoped` (`Guid? BranchId`) on an entity of a tenant-data context. `ApplyTenantFilters` adds the
named filter `Branch`:

```csharp
e => BranchFilterDisabled || e.BranchId == null || AllowedBranchIds.Contains(e.BranchId.Value)
```

* `BranchId = null` = tenant-wide row (visible to every scope).
* Tenant-wide admin views can use `.IgnoreBranchScope()` (tenant isolation stays).
* Writes: validate that the branch the caller writes to is inside its scope (`TenancyRequest.EnsureBranchesInScope`
  pattern) - the filter protects reads.

The Catalog sample's `Product` is branch-scoped (`products.branch_id`, index `(tenant_id, branch_id)`).

## Anti-escalation

Members holding `tenant.members.manage` / `tenant.roles.manage` can only grant roles whose permissions they hold
themselves and only assign branches inside their own scope; only the owner may change their own roles.
