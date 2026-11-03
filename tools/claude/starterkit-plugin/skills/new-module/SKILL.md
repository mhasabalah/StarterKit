---
name: new-module
description: Scaffold a new business module in the StarterKit modular monolith - Contracts, implementation, unit-test and integration-test projects, DbContext + design-time factory, <Module>Module.cs, solution/host/architecture-test wiring and the first migration. Use when the user asks to "add a module", "create a new bounded context", "scaffold module X" or "new module".
---

# New module

Creates `src/Modules/<Module>/` modelled on the **Catalog** sample, wired so every architecture test applies to it
from the first build.

**Not for:** a new aggregate inside an existing module (use `add-aggregate`) or a new endpoint (use `add-feature`).

## Inputs (ask only for what you cannot infer)

| Input | Example | Notes |
|---|---|---|
| `Module` | `Inventory` | PascalCase, singular or plural as the domain reads. Also the C# root namespace. |
| `schema` | `inventory` | lower-case Postgres schema, normally `Module.ToLowerInvariant()`. |
| Data plane | tenant-data / host | **tenant-data** (default for business data): `ITenantDataContext`, every entity `IMustHaveTenant`, RLS. **host**: platform/control-plane data, `IDbContext`, no tenant filters. |
| Publishes integration events? | yes / no | Adds the outbox now (cheaper than a second migration later). |
| First aggregate | `StockItem` | Optional - hand off to `add-aggregate` after the skeleton builds. |

## Steps

Templates for every file are in [references/templates.md](references/templates.md). Replace `{{Module}}`,
`{{module}}` (camel/lower) and `{{schema}}`.

1. **Projects** (`src/Modules/{{Module}}/`):
   - `{{Module}}.Contracts/` - csproj referencing **only** `Shared.Contracts`; `GlobalUsings.cs`; `{{Module}}ContractsRoot.cs`.
   - `{{Module}}/` - csproj (Shared.Core, Shared.Messaging, own Contracts, other modules' `.Contracts` only);
     `GlobalUsings.cs` ending with `{{Module}}Root`; `Resources/{{Module}}Resource.resx` + `.ar.resx`;
     `Options/{{Module}}Options.cs` if the module has settings.
   - `{{Module}}.Tests/` - xUnit + FluentAssertions + Moq; `TestSupport.cs`.
   - `{{Module}}.IntegrationTests/` - Testcontainers Postgres fixture calling the real `Add{{Module}}Module`.
2. **Persistence** (`{{Module}}/Data/`): `{{Module}}DbContext.cs`, `{{Module}}DbContextFactory.cs`, `Configurations/`,
   `Repository/`. Follow the `OnModelCreating` order in the template exactly - `ApplyTenantFilters(builder)` is LAST.
3. **Module registration**: `{{Module}}/{{Module}}Module.cs` with `Add{{Module}}Module` / `Use{{Module}}Module`
   (`UseMigration<{{Module}}DbContext>()`).
4. **Wire it into the host** - every one of these is required, missing one fails silently or fails a test:
   - `StarterKit.slnx`: a `<Folder Name="/src/Modules/{{Module}}/">` with the four projects.
   - `src/Api/Api.csproj`: `ProjectReference` to `{{Module}}.csproj`.
   - `src/Api/Program.cs`:
     - `using {{Module}};` (+ `using {{Module}}.Contracts;` if it has enums)
     - add `{{Module}}Root.AssemblyName` to `AddMassTransitWithAssemblies(...)` (consumers)
     - add `{{Module}}ContractsRoot.AssemblyName` to `AddEnumLookup(...)` if Contracts defines enums
     - `builder.Services.Add{{Module}}Module(configuration);` after `AddSubscriptionsModule`, before `AddNotificationModule`
       unless it must override another module's defaults
     - `app.Use{{Module}}Module();` after `UseTenancyModule()` (tenant-data seeding needs the Default tenant)
   - `src/Api.Tests/Architecture/ModuleBoundaryTests.cs` → add `"{{Module}}"` to `Modules`.
   - Tenant-data module: add `"{{Module}}DbContext"` to the expectation in
     `TenantIsolationArchitectureTests.Catalog_and_Attachment_are_tenant_data_contexts` (rename the test if useful).
   - Outbox: add `"{{Module}}DbContext"` to the outbox expectation in `MessagingArchitectureTests` (around line 187).
   - `appsettings.json`: a `"{{Module}}"` section if you added `{{Module}}Options`.
5. **Build** `dotnet build StarterKit.slnx` - fix everything before creating the migration.
6. **First migration** - run the `add-migration` skill (`InitialCreate`). Tenant-data: RLS for every table, including
   `outbox_messages`.
7. **Docs**: add a row to the module table in `README.md` and `wiki/INDEX.md`; create `wiki/Modules/{{Module}}.md`
   (purpose, aggregates, endpoints, events published/consumed, jobs, config).

## Guardrails this must satisfy

| Test (`src/Api.Tests/Architecture/`) | Rule |
|---|---|
| `ModuleBoundaryTests` | No reference to another module's implementation; Contracts reference no implementation; `Identity.Contracts` is off-limits (use `UserDirectory.Contracts`). |
| `MessagingArchitectureTests` | Contracts reference neither `Shared.Core` nor `Shared.Messaging`; outbox contexts registered with `AddOutbox<T>()` and vice versa. |
| `TenantIsolationArchitectureTests` | Tenant-data context: every entity `IMustHaveTenant`, all filters named, unique indexes include `tenant_id`, no parameterless `IgnoreQueryFilters()`. |
| `NamingConventionTests` | Handler/Command/Query/Validator/Event/EndPoint suffixes; no handlers in Contracts. |
| `PendingModelChangesTests` (`src/Api.E2ETests/Infrastructure/`) | The model matches the last migration. |

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Api.Tests                                   # architecture guardrails
dotnet test src/Modules/{{Module}}/{{Module}}.Tests
dotnet test src/Modules/{{Module}}/{{Module}}.IntegrationTests   # needs Docker
```

Then start the API (Aspire or compose) and confirm the schema `{{schema}}` exists and `/swagger` shows the module's
group once it has endpoints. Do not commit - leave the diff for the user.
