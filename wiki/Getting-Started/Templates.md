# `dotnet new` templates

StarterKit.Templates generates either a configured product or a module that the product host discovers.

## Goal

Choose generation switches, install one template source, then create a product or add a module.

## Prerequisites

Use the SDK pinned in `global.json`. Published-package installation needs GitHub Packages credentials; a local clone can be installed without that feed. Run module generation from a product root.

## At a glance

The tables below use template parameter definitions and CLI aliases. Standard CLI options such as `-n` (name) and `-o` (output) are engine options, not template parameters.

| Template | Short name | Creates |
|---|---|---|
| Product | `starterkit` | A renamed, configured solution: the whole template with your switches applied |
| Module | `starterkit-module` | A drop-in business module in an existing product: four projects, a manifest, an example aggregate with tests and its first migration |

## Steps

### Install

The pack is published to GitHub Packages on every `templates-v*` tag. GitHub Packages needs a login even to read, so
you need a GitHub token with the `read:packages` scope:
- With the GitHub CLI: `gh auth refresh -h github.com -s read:packages`, then pass `$(gh auth token)` as the password.
- Or a personal access token (classic) with `read:packages`.

Run these **outside** any repository: the template repository and every generated product have a `nuget.config` that
clears the machine's feeds, so inside them NuGet sees only nuget.org.

```bash
cd ~                                       # not inside a repository (see above)
dotnet nuget add source https://nuget.pkg.github.com/<owner>/index.json --name starterkit \
  --username <you> --password <token> --configfile "<your user NuGet.Config>"
dotnet new install StarterKit.Templates@1.0.0       # or StarterKit.Templates for the latest
```

- `--configfile` keeps the source in your user config. Without it, `dotnet nuget add source` writes to the nearest
  `nuget.config`, which could be a repository file you'd then commit by mistake. The user config is
  `%APPDATA%\NuGet\NuGet.Config` on Windows (the password is stored encrypted) and `~/.nuget/NuGet/NuGet.Config` on
  Linux and macOS (add `--store-password-in-clear-text`; there is no encryption there).
- Use `dotnet new install`, not `dotnet add package`: the "install" snippet on the package page is for libraries.
- `dotnet new install` reports "the package does not exist" when it can't see the feed: check that you are outside a
  repository and that the token has `read:packages`.

Or install straight from a clone, which always gives the checked-out version (for working on the template itself):

```bash
dotnet new install ./StarterKit          # both templates: the repository root holds them
```

| Task | Command |
|---|---|
| See what is installed and from where | `dotnet new uninstall` (lists every installed pack) |
| Update to the latest published version | `dotnet new update` (outside a repository) |
| Remove the published pack | `dotnet new uninstall StarterKit.Templates` |
| Switch from a clone to the package | `dotnet new uninstall <clone path>`, then install the package |

Install the templates from one place only: the clone and the package provide the same templates and conflict.

### New product

```bash
dotnet new starterkit -n Acme
dotnet new starterkit -n Acme --sample false --multitenancy shared --transport RabbitMq --storage S3 --github-owner acme-inc
```

The name replaces every spelling of the template's name. Each spelling maps as follows:

| Spelling | Example (`-n Acme`) | Where it appears |
|---|---|---|
| `StarterKit` | `Acme` | The `.slnx`, identifiers, titles |
| `starterkit` | `acme` | The connection-string key, container names, the database, the JWT issuer, the Redis instance, `/opt/acme`, the plugin |
| `STARTERKIT` | `ACME` | Environment-variable prefixes |

Module namespaces (`Identity`, `Tenancy`, ...) carry no prefix and stay as they are. Every `UserSecretsId` and the
compose project GUID are new per project.

| Name | Type / values | Default | Effect |
|---|---|---|---|
| `--sample` | bool | `true` | Include the Catalog reference module (products, categories, tags, wishlist) and everything that uses it. |
| `--multitenancy` | choice: `off`, `shared`, `dedicated` | `off` | Multi-tenancy preset (MultiTenancy:Enabled / DefaultIsolation). It can be switched later without a migration. |
| `--transport` | choice: `PostgreSql`, `RabbitMq` | `PostgreSql` | Production message transport (MESSAGING_TRANSPORT in .env.example). |
| `--storage` | choice: `Local`, `AzureBlob`, `S3` | `Local` | Attachment storage provider (Attachment:Storage:Provider). |
| `--ai-tooling` | bool | `true` | Include the Claude Code plugin (skills, reviewer agent, hooks), Spec Kit and the other agent configs. |
| `--ci` | bool | `true` | Include the GitHub Actions workflows. |
| `--deploy` | bool | `true` | Include the VPS deployment kit: deployment/, Nginx, image build and deploy jobs, backup/rollback/health workflows. |
| `--github-owner` | text | `CHANGE_ME` | GitHub user or organization that owns the new repository (CODEOWNERS, issue links). |

`--sample false` removes the reference module, its E2E flows, sample webhook/dashboard contributions and conditional documentation rows.

The product definitions are `.template.config/template.json` and `.template.config/dotnetcli.host.json` in the template repository. Storage changes the Docker production preset; development and tests remain Local. Transport changes `.env.example`; development and tests remain InMemory. `--deploy false` keeps local compose. `--ai-tooling false` removes agent configs and Claude workflows; `--ci false` removes all workflows. Multi-tenancy can change later without a schema migration.

Then follow [New-Project](New-Project.md) from "Configure": `.env`, secrets, the GitHub environment.

### New module

Run it from the product root:

```bash
dotnet new starterkit-module -n Orders
dotnet new starterkit-module -n Billing --data-plane host --outbox
dotnet new starterkit-module -n Inventory --example false
```

| Name | Type / values | Default | Effect |
|---|---|---|---|
| `--data-plane` | choice: `tenant`, `host` | `tenant` | `tenant`: `ITenantDataContext`, every entity `IMustHaveTenant`, forced RLS. `host`: platform/control-plane data without tenant filters or RLS. |
| `--outbox` | bool | `false` | Adds `AddOutbox<T>()` and the outbox table/migration. Includes an example integration event when the example aggregate is included. |
| `--example` | bool | `true` | Include the example Item aggregate (CRUD, concurrency, tests). false gives an empty skeleton. |

The module definitions are `templates/module/.template.config/template.json` and its adjacent `dotnetcli.host.json`. There is no `--schema` switch: the context uses the lower-case module name. `--example false --outbox true` still includes an outbox migration; only an empty skeleton without outbox has no migration. Computed symbols are not CLI switches.

Nothing else needs wiring ([ADR-0019](../Decisions/0019-module-manifests-and-discovery.md)):
- A post-action adds the four projects to the solution; if it cannot, follow its manual instructions using `dotnet sln add`.
- `Api.csproj` references the module by glob.
- `builder.AddModules()` finds its manifest.
- The architecture tests take the module list from the host.

Build, run the tests, then grow the module with the `add-aggregate` and `add-feature` skills.

<!--#if (templateSource)-->
## Changing the template

See [Changing-The-Template](Changing-The-Template.md): the conditional-code rules, the module migration sets and how
CI proves every generated product and module.

<!--#endif-->
## Versioning

The pack version comes from the tag: pushing `templates-v1.4.0` builds, tests and publishes `StarterKit.Templates
1.4.0`. A published version is never replaced, so every release gets a new number. `dotnet new update` moves your
machine to the latest one; new products then use it.

A generated product doesn't track the template after it is created. Port later template fixes by hand, using the
template's changelog in the PR descriptions.

## Verify

Build the generated `.slnx`, run `dotnet test src/Api.Tests`, and run module unit and integration projects (Docker required for integration). [New project](New-Project.md) contains the full checklist.

## Pitfalls

Generation presets do not configure provider credentials. Fill storage, broker and deployment settings before use. Generated products are independent of the installed pack.

## Related

[Project structure](../Architecture/ProjectStructure.md), [Module discovery](../Decisions/0019-module-manifests-and-discovery.md), and `/starterkit:new-module`.
