---
name: new-module
description: Scaffold a new business module in the StarterKit modular monolith with `dotnet new starterkit-module` (or by hand) - Contracts, implementation, unit-test and integration-test projects, DbContext + design-time factory, <Module>Module.cs, solution/host/architecture-test wiring and the first migration. Use when the user asks to "add a module", "create a new bounded context", "scaffold module X" or "new module".
---

# New module

Creates `src/Modules/<Module>/`: four projects, a manifest the host discovers, and (by default) an example aggregate
with CRUD, optimistic concurrency, tenant isolation, unit and integration tests, and its first migration. Every
architecture test applies to it from the first build.

**Not for:** a new aggregate inside an existing module (use `add-aggregate`) or a new endpoint (use `add-feature`).

## Inputs (ask only for what you cannot infer)

| Input | Example | Notes |
|---|---|---|
| `Module` | `Inventory` | PascalCase letters only (it becomes the namespace, the schema `inventory` and the event URN prefix). Must not clash with an existing module. |
| Data plane | `tenant` / `host` | **tenant** (default for business data): `ITenantDataContext`, every entity `IMustHaveTenant`, forced RLS. **host**: platform data, no tenant filters. |
| Publishes integration events? | yes / no | `--outbox`: adds the outbox now (cheaper than a second migration later) and an example integration event. |
| Example aggregate? | yes / no | `--example false` for an empty skeleton when the first aggregate is known: then hand off to `add-aggregate`. |

## Preferred path: the module template

From the product root (the folder with the `.slnx`):

```bash
dotnet new starterkit-module -n <Module> [--data-plane host] [--outbox] [--example false]
```

If the template is not installed: `dotnet new install StarterKit.Templates` (or `dotnet new install
<template-repo>/templates/module`). See `wiki/Getting-Started/Templates.md`.

Then:
1. Check the four projects were added to the `.slnx` (the template's post-action does it; otherwise `dotnet sln add`).
2. `dotnet build <Product>.slnx`, `dotnet test src/Api.Tests`, the module's `.Tests` and `.IntegrationTests`.
3. Rename or replace the example `Item` aggregate with the real one (`add-aggregate`), then delete the example
   migration and create the real `InitialCreate` (`add-migration`) **before the first production deploy**. Re-add
   the `EnableTenantRowLevelSecurity` calls for every tenant table (the example migration shows them).
4. Permissions: the example endpoints use `.RequireTenantMember()` (tenant) or `AdminOnly` writes (host). Give the
   module its own permission with `add-permission`.
5. Docs: add a row to the module tables in `README.md` and `wiki/INDEX.md`, and create `wiki/Modules/<Module>.md`.

## Manual path (no template)


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
   (`UseMigration<{{Module}}DbContext>()`), plus `{{Module}}/{{Module}}ModuleManifest.cs` (`ModuleOrder.Default`,
   `EnumAssemblies` if Contracts has enums) - see the template.
4. **Host wiring** - discovery does it (ADR-0019): `Api.csproj` references `src/Modules/*/*/*.csproj` by glob,
   `builder.AddModules()` registers the module, its consumers and enums, and the architecture tests take the module
   list from the host. Do NOT edit `Program.cs`, `Api.csproj`, the Dockerfile or `ModuleBoundaryTests`. Still do:
   - `StarterKit.slnx`: a `<Folder Name="/src/Modules/{{Module}}/">` with the four projects.
   - Tenant-data module: add `"{{Module}}DbContext"` to the expectation in
     `TenantIsolationArchitectureTests` only if you want it pinned as tenant data (optional; the rules apply anyway).
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
group once it has endpoints. Commit only when the user asks, on a feature branch (CLAUDE.md > Rules > Git); otherwise leave the diff for review.
