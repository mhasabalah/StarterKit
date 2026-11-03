---
name: add-aggregate
description: Add a new aggregate root or entity to an existing StarterKit module - DDD entity with factory + invariants + domain events, EF Core configuration, repository, exceptions, DbSet, tenant awareness (IMustHaveTenant / IBranchScoped) and the migration hand-off. Use for "add entity X", "new aggregate", "new table", "model X in module Y".
---

# Add an aggregate / entity

**Not for:** endpoints (`add-feature`), a whole new module (`new-module`).

## Inputs

- Module and aggregate name; root vs child entity vs lightweight lookup.
- Properties (types, max lengths, required), localized names (`LocalizedText` → `name_en`/`name_ar`)?
- Uniqueness rules (→ unique index, **tenant-first** in tenant-data contexts).
- Lifecycle: status state machine? soft delete really needed? branch-scoped?
- Which facts other code reacts to → domain events (same module) / integration events (other modules).

## Decide the shape

| Kind | Base | Repository? | Example |
|---|---|---|---|
| Aggregate root (has invariants, children, events) | `Aggregate<Guid>` | yes, `I<X>Repository` | `Catalog/Domains/Products/Models/Product.cs` |
| Child entity (only changed through its root) | `Entity<Guid>` | no - via root | `ProductImage` in `ProductChildren.cs` |
| Lookup (simple bilingual list) | `Entity<Guid>` | no - handlers use DbContext | `Catalog/Domains/Tags/Models/Tag.cs` |

Tenant-data module (`ITenantDataContext`): EVERY entity (children too) implements `IMustHaveTenant`
(`public Guid TenantId { get; set; }` - stamped by `TenantEntityInterceptor`, never set it). Add `IBranchScoped`
(`public Guid? BranchId { get; set; }`, null = tenant-wide) only if data belongs to a branch.

## Steps

Templates: [references/templates.md](references/templates.md).

1. **Model** `Domains/<X>s/Models/<X>.cs`: private parameterless ctor (EF), `private set`, `const` max lengths,
   `static Create(...)` factory that validates and raises `<X>CreatedEvent`, intention-revealing methods
   (`Rename`, `Publish`, `Archive`) that guard invariants and raise events. Collections as private `List<T>` +
   `IReadOnlyCollection<T>`. Never touch `Version`. Use `TimeProvider` (passed in) or `DateTime.UtcNow` inside the
   entity consistently with neighbours; inject `TimeProvider` in handlers/jobs.
   Need a non-nullable created date? `public new DateTime CreatedAt { get; private set; }`.
2. **Events** `Domains/<X>s/Events/<X>Events.cs`: `public record <X>CreatedEvent(Guid <X>Id, ...) : IDomainEvent;`
   (ids + minimal data). Handlers in `Domains/<X>s/EventHandlers/`.
3. **Exceptions** `Domains/<X>s/Exceptions/<X>Exceptions.cs`: `<X>NotFoundException : NotFoundException`,
   `Invalid<X>Exception : BadRequestException`, `<X>AlreadyExistsException : ConflictException` (→ 404/400/409).
4. **EF configuration** `Data/Configurations/<X>Configuration.cs`: key, owned `LocalizedText` columns, max lengths,
   enums `.HasConversion<string>().HasMaxLength(n)`, no `HasDefaultValue` on bools, relationships with explicit
   `OnDelete`, indexes. **Child entities** (added through a parent's collection) with domain-assigned Guid ids need
   `builder.Property(x => x.Id).ValueGeneratedNever()`. Unique index in tenant data: `HasIndex(x => new { x.TenantId, x.Code }).IsUnique()
   .HasDatabaseName(<X>Configuration.CodeIndexName)`. Uniqueness on an **owned** column (e.g. `name_en`) cannot
   include `tenant_id` in EF - create it with raw SQL in the migration (see `Tag` + `AddMultiTenancy` migration).
5. **DbSet** in `<M>DbContext`: `public DbSet<<X>> <X>s => Set<<X>>();`. Soft delete, only if required:
   `Entity<T>` already has `IsDeleted`; enable the filter with `builder.FilterSoftDeletedProperties()` (it applies
   to every entity of the context), add `DeletedAt` and `Delete()`/`Restore()` domain methods as `Product` does,
   and read deleted rows with `IgnoreNonIsolationFilters()`. **Never** use `IgnoreQueryFilters()`.
6. **Repository** (roots only) `Data/Repository/<X>Repository.cs`: interface + class in one file; `GetByIdAsync`
   includes children (`AsSplitQuery()`), `AddAsync`, `SaveChangesAsync` translating
   `ex.IsUniqueViolation(<X>Configuration.<Index>Name)` into `<X>AlreadyExistsException`. Register
   `services.AddScoped<I<X>Repository, <X>Repository>()` in `<M>Module.cs`.
7. **GlobalUsings**: add `global using <M>.Domains.<X>s.Models;` and `.Exceptions;`.
8. **Migration**: run the `add-migration` skill. Tenant-data → `EnableTenantRowLevelSecurity` for each new table.
9. **Tests**: domain unit tests for every factory/method/invariant and event raised (`write-tests`).

## Guardrails

- `TenantIsolationArchitectureTests`: tenant entity without `IMustHaveTenant`, unnamed query filter, or unique index
  without `tenant_id` fails the build pipeline.
- `NamingConventionTests`: `<X><PastTense>Event`.
- `PendingModelChangesTests`: model and migration must match.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Modules/<M>/<M>.Tests --filter "FullyQualifiedName~<X>"
dotnet test src/Api.Tests
```
