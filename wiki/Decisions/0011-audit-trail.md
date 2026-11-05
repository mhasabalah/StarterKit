# 0011. Record entity changes with an opt-in audit trail written in the same SaveChanges

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

Support, compliance and tenant admins need to answer "who changed what, when, and from where" for business data such as
products. `CreatedBy` / `LastModifiedBy` only say who touched a row last. `TenantAuditLog` records administrative
actions written by hand, so it only covers what a handler remembers to record.

## Decision

- Entities opt in with the `IAuditable` marker (`Shared.Core.Auditing`). `[AuditIgnore]` removes a property.
- `AuditTrailInterceptor` (registered by `AddCustomDbContext` after `TenantEntityInterceptor`, before the outbox) adds
  one `audit_entries` row per changed auditable entity to the SAME `SaveChanges`, so it is committed or rolled back with the change.
- A row holds entity type and id, `Added | Modified | Deleted`, a JSON diff `{ "Prop": { "old", "new" } }`, actor,
  impersonator (`act` claim), correlation id, IP and tenant.
- Each context maps the table like the outbox: `builder.AddAuditTrail()` (host) or `AddTenantAuditTrail()`
  (tenant-data: tenant-owned row, named filter, RLS in the migration).
- Secrets are never recorded: `[AuditIgnore]`, bookkeeping columns, and any property whose name contains
  `Password`, `Secret`, `Token` or `Hash`. Strings are cut (`AuditTrail:MaxValueLength`) and one entry is capped
  (`AuditTrail:MaxChangesBytes`, then only property names are kept).
- Append-only: the interceptor rejects modifying or deleting audit rows through the change tracker (host scope
  excepted). Retention is a set-based delete (`PurgeAuditEntriesAsync`, `AuditTrail:RetentionDays`, default 365) run by a
  per-module job (Catalog: `PurgeAuditEntriesJob`).
- Reference adopter: Catalog `Product` and `Category`; `GET /api/v1/products/{id}/history` (`tenant.audit.read`).

## Consequences

- Auditing a new aggregate is one marker plus one migration. `AuditTrailArchitectureTests` fails when a context maps
  `IAuditable` entities without the audit table, and the interceptor throws at the first save too.
- Every audited write adds one insert. Child collections (images, tags) and `ExecuteUpdate` / `ExecuteDelete` changes
  are not captured.
- Diffs are data, not a domain event log: do not build behaviour on them.

## Alternatives considered

- Database triggers: miss the application actor, impersonator and correlation id.
- Event sourcing / domain-event log: far larger change, and domain events are not emitted for every property.
- A shared audit schema: would break per-tenant dedicated databases and RLS; the table lives next to the data.
