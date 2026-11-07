# 0019. Modules plug in through a manifest the host discovers from the build graph

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

Adding a module meant hand edits in six places:
- `Program.cs`: the Add and Use calls, the MassTransit assembly list and the enum lookup list.
- `Api.csproj`.
- The Dockerfile restore list.
- `ModuleBoundaryTests.Modules`.

Forgetting one failed silently. A module without a consumer registration loses its messages, and a missing Dockerfile
line only shows up as a cold cache. It also made a `dotnet new` module template impossible without patching C#
files as text (backlog G23). The startup order rules lived only in comments in `Program.cs` and CLAUDE.md.

## Decision

- **One manifest per module.** `<M>ModuleManifest : ModuleBase` (`Shared.Core.Modularity.IModule`) sits next to
  `<M>Module.cs` and is marked with `[assembly: ModuleManifest(typeof(<M>ModuleManifest))]`. It declares:
  - `Name`, which is the assembly name
  - `Order`
  - `ConsumerAssemblies`, by default the module assembly
  - `EnumAssemblies`, normally the module's Contracts
  - `AddServices(IHostApplicationBuilder)`
  - `Initialize(WebApplication)`: migrations, seeds, recurring jobs and hubs, but never middleware

  It delegates to the existing `Add<M>Module` / `Use<M>Module` extensions, which stay so that integration fixtures
  can keep calling them.
- **The module list is the build graph.** `Api.csproj` references `..\Modules\*\*\*.csproj` by glob, leaving out
  `*.Contracts`, `*.Tests` and `*.IntegrationTests`. A target turns each module reference into
  `[assembly: ModuleAssembly("<M>")]` on the host. At startup, `ModuleCatalog.FromHost` reads those attributes, loads
  the assemblies and instantiates the manifests. There is no `deps.json` or folder scanning at runtime, and no risk
  that the compiler drops an unused reference.
- **Order is data.** `ModuleOrder` constants encode the old `Program.cs` order:

  | Module | Order |
  |---|---|
  | Identity | 0 |
  | Tenancy | 100 |
  | Attachment | 200 |
  | Catalog | 300 |
  | Subscriptions | 400 |
  | Webhooks | 500 |
  | DataTransfer | 600 |
  | **new modules (`Default`)** | **700** |
  | Notification | 900 |

  Modules run in `(Order, Name)` order for both registration and init.
- **Fail fast.** A referenced module with no manifest, a manifest from another assembly, a manifest that isn't an
  `IModule`, or two modules with the same name each throw at startup with a clear message.
- **Pipeline stays explicit.** Middleware position isn't module init. Identity is split into
  `UseIdentityPipeline(afterAuthentication)` (authentication → tenant resolution → authorization, in `Program.cs`) and
  `UseIdentityModule()` (migrations, called by the manifest).
- `Program.cs` calls `builder.AddModules()` and `app.UseModules()`.
- The Dockerfile restore stage copies every `*.csproj` from a helper stage, so it never lists projects by hand.

## Consequences

- A new module is a folder, and the host, MassTransit, the enum lookup and the Docker build pick it up with zero
  edits. This is what makes `dotnet new starterkit-module` a drop-in.
- The architecture tests take their module list from the host (`ModuleCatalog`). `ModuleDiscoveryTests` checks four
  things:
  - The `src/Modules` folders and the discovered set are the same.
  - Each module has exactly one manifest, named after it.
  - Identity starts first and Notification last.
  - Tenancy starts before every tenant-data module and Subscriptions, and Webhooks starts after the modules whose
    events it forwards.
- Startup now loads module assemblies by name through reflection. This is eight `Assembly.Load` calls, done once.
- The enum lookup scans assemblies in module order. Duplicate enum names would resolve first-wins, but none exist
  today.
- **Rejected options:**
  - A Roslyn source generator: compile-time, but it adds an analyzer project to maintain for no practical gain over
    MSBuild-generated attributes.
  - Scanning `deps.json`: it depends on the deployment layout.
  - Text-patching `Program.cs` from the template: brittle markers.
