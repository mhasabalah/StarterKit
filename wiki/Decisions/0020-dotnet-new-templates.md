# 0020. The repository is the product template; modules come from a generated, CI-verified module template

## Status

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

Starting a product meant copying the repository, running a case-sensitive search and replace, and walking a manual
checklist (backlog #12). Adding a module meant hand-writing four projects from snippets (#13). Both drifted from the
real code, and neither was tested. G23 asks for `dotnet new` templates. The hard parts:

- keeping the template repository itself buildable and fully tested
- optional parts that reach into many files (the Catalog sample is used by about 45 files outside its folder)
- a module whose first migration must match its model in every option combination

## Decision

- **The repository is the product template.** `.template.config/template.json` sits at the root, with
  `sourceName: StarterKit`.
  - The name is replaced in three spellings: identity, the lower-case form (built in), and an upper-case `nameUpper`
    for `STARTERKIT_*` environment variables. A lower-case `fileRename` handles paths such as
    `tools/claude/starterkit-plugin`.
  - Every `UserSecretsId` and the compose project GUID are regenerated.
  - There is no copy of the code to keep in sync.
- **Optional parts are conditional in place, compiled in the template.**
  - C# uses `#if (SAMPLE)`. `Directory.Build.props` defines `SAMPLE` only when `.template.config` exists, inside a
    `<!--#if (templateSource)-->` block that never reaches a product. So `dotnet build` / `dotnet test` of the template
    builds and tests every optional part.
  - Project files, `.slnx`, Markdown and YAML use comment markers.
  - `appsettings*.json` and `.env*` use an `uncomment` action, so the inactive branch is a commented line, not a
    duplicate key.
  - Whole files that belong to one switch are `sources.modifiers` exclusions.
- **Switches:**
  - `--sample`, `--multitenancy`, `--transport`, `--storage`, `--ai-tooling`, `--ci`, `--deploy`, `--github-owner`
  - Presets land where they take effect without breaking dev or tests:
    - multi-tenancy in `appsettings.json` (the single-tenant E2E factory pins it off)
    - storage in `appsettings.Docker.json` (production)
    - transport in `.env.example`
- **Tests are sample-independent where they can be.** A test that only needs some vehicle uses one that every product
  has:
  - the Webhooks tests forward a Subscriptions event, not the product event
  - the API-key escalation test uses the AdminOnly dashboard
  - Product-specific tests sit behind `#if (SAMPLE)`.
- **One connection-string name.** `Shared.Core.Data.DatabaseDefaults.ConnectionStringName` replaces the
  `"starterkit"` literals. Renaming a product changes it in one place, and a generated module (which cannot know the
  product's name) uses the constant.
- **The module template** (`templates/module`, `sourceName: ModuleName`) is a complete module:
  - four projects and the manifest of [ADR-0019](0019-module-manifests-and-discovery.md), so there is no host wiring
  - an example `Item` aggregate with create, read, paged list, versioned update and delete
  - unit tests and Testcontainers integration tests with RLS
  - options `--data-plane tenant|host`, `--outbox` and `--example`
- **Migrations are generated, not hand-merged.** Each option combination that has a model ships its own `InitialCreate`
  set:
  - The sets live under `templates/module/_migrations/<combo>/` and are selected by conditional `sources`.
  - Each set was produced by `dotnet ef` from that exact combination, with `EnableTenantRowLevelSecurity` added for
    tenant data.
  - The empty skeleton without an outbox has no model, so it ships no migration.
- **Distribution and proof.** `templates/StarterKit.Templates.csproj` packs both templates. The `templates.yml`
  workflow packs, installs and generates four products with different switches, plus modules with different options
  inside them. Each one must:
  - contain no leftover template name or marker
  - build with warnings as errors
  - pass the architecture tests, the module unit and integration tests, the E2E suite (migrations match the model,
    RLS coverage, boot) and the docs check

  A `templates-v*` tag publishes the verified pack to GitHub Packages.

## Consequences

- A product is one command, and a module is one command plus `dotnet build`. What the templates produce is tested on
  every PR, so it can't silently rot.
- Template-repo changes follow three rules (see [Templates](../Getting-Started/Templates.md#changing-the-template)):
  - optional code is conditional, not deleted
  - never an MSBuild `Condition` attribute on an element that must survive: `dotnet new` evaluates it as a template
    condition and drops the element
  - keep JSON/`.env` conditional blocks small and free of `//` / `#` in values
- Changing the example module's model means regenerating its migration sets with
  `scripts/regenerate-module-migrations.sh`. The CI matrix proves the result.
- Generated products don't track the template. Later template fixes are ported by hand.

## Alternatives considered

- A separate template copy: two code bases.
- A post-generation script that runs `dotnet ef`: it needs `--allow-scripts`, network access and a build, and it
  isn't drop-in.
- Hand-written conditional regions inside one migration snapshot: unverifiable by eye.

## Related

[Templates](../Getting-Started/Templates.md).
