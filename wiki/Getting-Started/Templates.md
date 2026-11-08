# `dotnet new` templates

StarterKit ships as a template pack, `StarterKit.Templates`, with two templates:

| Template | Short name | Creates |
|---|---|---|
| Product | `starterkit` | A renamed, configured solution: the whole template with your switches applied |
| Module | `starterkit-module` | A drop-in business module in an existing product: four projects, a manifest, an example aggregate with tests and its first migration |

## Install

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

## New product

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

Module namespaces (`Identity`, `Catalog`, ...) carry no prefix and stay as they are. Every `UserSecretsId` and the
compose project GUID are new per project.

| Switch | Values (default) | Effect |
|---|---|---|
| `--sample` | `true` / `false` | `false` leaves out the Catalog reference module and everything that uses it (its E2E flows, the product webhook, dashboard stats, docs rows). The module template then generates your reference code. |
| `--multitenancy` | `off` / `shared` / `dedicated` (`off`) | `MultiTenancy:Enabled` and `DefaultIsolation` in `appsettings.json` and `.env.example`. The single-tenant E2E suite pins it off, and the tenant suites switch it on. Can be changed later without a migration ([Overview](../MultiTenancy/Overview.md)). |
| `--transport` | `PostgreSql` / `RabbitMq` (`PostgreSql`) | `MESSAGING_TRANSPORT` in `.env.example`. Dev and tests stay on `InMemory` ([Messaging](../Architecture/Messaging.md)). |
| `--storage` | `Local` / `AzureBlob` / `S3` (`Local`) | `Attachment:Storage:Provider` in `appsettings.Docker.json` (production). Dev and tests stay on Local disk, and the bucket or container settings are yours to fill in ([Storage providers](../Attachment/Storage-Providers.md)). |
| `--ai-tooling` | `true` / `false` | `false` leaves out the Claude Code plugin, `.claude`, Spec Kit, the other agent configs and the Claude workflows |
| `--ci` | `true` / `false` | `false` leaves out `.github/workflows` |
| `--deploy` | `true` / `false` | `false` leaves out `deployment/`, the Nginx part of `infra/`, the image build and deploy jobs, and the backup, rollback and health workflows. Local compose stays. |
| `--github-owner` | text (`CHANGE_ME`) | The GitHub user or organization in `CODEOWNERS` and the issue links |

Then follow [New-Project](New-Project.md) from "Configure": `.env`, secrets, the GitHub environment.

## New module

Run it from the product root:

```bash
dotnet new starterkit-module -n Orders
dotnet new starterkit-module -n Billing --data-plane host --outbox
dotnet new starterkit-module -n Inventory --example false
```

| Option | Values (default) | Effect |
|---|---|---|
| `--data-plane` | `tenant` / `host` (`tenant`) | `tenant`: `ITenantDataContext`, every entity `IMustHaveTenant`, RLS in the migration. `host`: platform data, no tenant filters. |
| `--outbox` | `true` / `false` (`false`) | Adds the transactional outbox (`AddOutbox<T>()` plus the outbox table in the migration) for modules that publish integration events |
| `--example` | `true` / `false` (`true`) | `false` gives an empty skeleton: no aggregate and no migration yet |
| `--schema` | text (the lower-case name) | The Postgres schema |

Nothing else needs wiring ([ADR-0019](../Decisions/0019-module-manifests-and-discovery.md)):
- The four projects are added to the `.slnx`.
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
