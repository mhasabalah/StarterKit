# `dotnet new` templates

StarterKit ships as a template pack, `StarterKit.Templates`, with two templates:

| Template | Short name | Creates |
|---|---|---|
| Product | `starterkit` | A renamed, configured solution: the whole template with your switches applied |
| Module | `starterkit-module` | A drop-in business module in an existing product: four projects, a manifest, an example aggregate with tests and its first migration |

## Install

```bash
# From GitHub Packages (published on every templates-v* tag; needs a PAT with read:packages)
dotnet nuget add source https://nuget.pkg.github.com/<owner>/index.json --name starterkit --username <you> --password <PAT>
dotnet new install StarterKit.Templates

# Or straight from a clone (always the checked-out version)
dotnet new install ./StarterKit          # the repository root
dotnet new install ./StarterKit/templates/module
```

`dotnet new uninstall StarterKit.Templates` (or the folder path) removes it.

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
1.4.0`. A generated product doesn't track the template after it is created. Port later template fixes by hand, using
the template's changelog in the PR descriptions.
