# 0005. Tenant isolation: named EF filters + PostgreSQL RLS, shared or dedicated databases

Named EF filters and PostgreSQL row-level security isolate tenant data in shared or dedicated databases.

## Status

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

The template must serve single-tenant products by default, and SaaS products with strong tenant isolation when
`MultiTenancy:Enabled=true`, without forking the code.

## Decision

Tenancy is split into two planes.

- **Control plane** (Tenancy module, host context): tenants, branches, memberships, roles.
- **Data plane** (`ITenantDataContext`): every entity is `IMustHaveTenant`, with a named `Tenant` filter (plus
  `Branch`), and every table has **PostgreSQL row-level security** as a second layer. `TenantEntityInterceptor` stamps
  `TenantId` and blocks cross-tenant writes.

The ambient tenant (`TenantScope`) flows through HTTP, Hangfire and MassTransit. A tenant may live in the shared
database or in a dedicated one, and can be moved between them. Single-tenant mode uses the Default tenant.

## Consequences

- Isolation holds even when a query forgets a filter (RLS), and the same code serves both modes.
- Every new tenant table needs RLS in its migration and tenant-first unique indexes. Parameterless
  `IgnoreQueryFilters()` is banned.
- **Enforced by:** `TenantIsolationArchitectureTests`, the multi-tenant E2E tests, and the `add-aggregate` /
  `add-migration` skills. Details: [MultiTenancy](../MultiTenancy/Overview.md).

## Alternatives considered

- **Schema per tenant:** migrations multiply with every tenant.
- **Database per tenant only:** too expensive for small tenants. It is still offered as an option.

## Related

[Isolation and Dedicated Databases](../MultiTenancy/Isolation-and-Dedicated-Databases.md).
