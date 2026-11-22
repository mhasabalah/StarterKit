# Starting a new project from StarterKit

Generate, configure and verify a product from the StarterKit template before adding its first business module.

## Goal

Finish with a renamed solution, working local dependencies and a discovered module. `/starterkit:new-project` follows this checklist.

## Prerequisites

Use the SDK pinned in `global.json`, Docker for Aspire and database tests, and Git Bash for these commands. Install the pack using [Templates](Templates.md#install); package installation needs feed credentials, while a clone can be installed locally.

## Steps

### 1. Generate

Decide the switches first, then generate. See [Templates](Templates.md) for every switch.

| Decision | Switch | Where it lands |
|---|---|---|
| Name | `-n Acme` (slug `acme`, env prefix `ACME_`) | Everything |
| Catalog reference module | `--sample true\|false` | The `src/Modules/Catalog` folder and everything that uses it |
| AI agents | `--agentic true\|false` | The `src/Modules/Agentic` folder (and the Shipping sample) and everything that uses them |
| Multi-tenancy | `--multitenancy off\|shared\|dedicated` | `MultiTenancy:*` in `appsettings.json` and `.env.example` ([Overview](../MultiTenancy/Overview.md)) |
| Messaging transport (prod) | `--transport PostgreSql\|RabbitMq` | `MESSAGING_TRANSPORT` in `.env.example` ([Messaging](../Architecture/Messaging.md)) |
| File storage | `--storage Local\|AzureBlob\|S3` | `Attachment:Storage:Provider` in `appsettings.Docker.json` (production) ([Storage-Providers](../Attachment/Storage-Providers.md)) |
| Agent tooling, CI, VPS deploy kit | `--ai-tooling`, `--ci`, `--deploy` | Folders kept or left out |
| Repository owner | `--github-owner acme-inc` | `CODEOWNERS`, the issue links |

```bash
dotnet new install StarterKit.Templates                  # once; source setup: Templates.md#install (or: dotnet new install <path-to-template-repo>)
dotnet new starterkit -n Acme --multitenancy shared --github-owner acme-inc
cd Acme && git init
```

The pack comes from GitHub Packages: add its NuGet source once, outside any repository, as shown in
[Templates → Install](Templates.md#install).

`LICENSE` is MIT with the template author's copyright. Keep it for the code you reuse (MIT requires the notice), and
add your own license and copyright for the new product.

**Without the template pack** (fallback):
1. Copy the repository without `.git`, `bin/`, `obj/` and `node_modules/`.
2. Rename `StarterKit.slnx`.
3. Do a case-sensitive replace of `StarterKit`, `starterkit` and `STARTERKIT`, excluding `Data/Migrations/`.
4. Resolve the template's conditional markers by hand (the template repository's Changing-The-Template page lists
   them).

The template does all of this for you, so prefer it.

### 2. Configure

1. Run `cp .env.example .env` and replace every `CHANGE_ME`. Never commit `.env`.
2. Create `secrets/*.txt` (see `secrets/README.md`, or run `./deployment/secrets-setup.sh` with the deploy kit). See
   [Secrets](../deployment/Secrets.md).
3. With the deploy kit: set `DOMAIN`, plus the Nginx `server_name` and admin IP allow-list in
   `deployment/nginx/conf.d/app-production.conf`.
4. Review `src/Api/appsettings.json` and `src/Api/appsettings.Docker.json`, plus environment/secret overrides: payment gateways, email, push and malware scanning. There is no checked-in `appsettings.Production.json`.

#### Product decisions before the first production migration

The template ships neutral defaults for these. Each one is cheap to change now and expensive after go-live:

1. **Attachment enums.** `EntityType` and `AttachmentCategory`
   (`src/Modules/Attachment/Attachment.Contracts/Attachments/Dtos/AttachmentEnums.cs`) are stored as strings and
   referenced by clients. Replace the generic values (`User`, `Product`, `Company`, `Other`, ...) with your domain's.
2. **Who may manage the subscription.** The subscriber endpoints in
   `src/Modules/Subscriptions/Subscriptions/Domains/*/Features/` declare `.RequireAuthorization()` (any signed-in
   user). The subscriber is resolved per request (`ISubscriberIdProvider`): in multi-tenant mode, managing billing
   needs `tenant.billing.manage`. If only owners may buy, cancel or upload proofs in your product, add a stricter gate
   (a tenant permission or `.DisallowImpersonation()`) to those endpoints.
3. **WhatsApp templates.** `WhatsApp:Templates` in `src/Api/appsettings.json` maps every message to a Meta template
   name, language and body parameters. The defaults are Arabic sample names. Register your own templates with Meta
   and set the names and `LanguageCode` per environment.
4. **Admin allow-list.** In `deployment/nginx/conf.d/app-production.conf`, `/swagger` and `/scalar` allow only
   the loopback address, and `/seq/`, `/grafana/`, `/prometheus/` and `/jaeger/` deny everyone. Add an `allow` line with your
   operators' IPs (or a VPN range) to each location you need, above its `deny all;`.

### 3. Migrations

The generated project keeps the template's migrations, and they match its model (the `PendingModelChangesTests` E2E
test checks it). Regenerate them only if you change entities before your first production deploy. In that case,
delete the affected `Data/Migrations` folders and create one `Initial<Module>` migration per DbContext
([Reference/Migrations](../Reference/Migrations.md)). For tenant-data contexts, re-add the
`EnableTenantRowLevelSecurity` calls, including `outbox_messages`.

### 4. GitHub

1. Create the `production` environment with required reviewers.
2. Add the repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`,
   `BACKUP_BUCKET`) and the optional secrets (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`,
   `CLAUDE_CODE_OAUTH_TOKEN`). See [CI-CD](../deployment/CI-CD.md#github-setup).
3. Register the self-hosted runner with the `deploy-only` label; see
   [Self-Hosted-Runner](../deployment/Self-Hosted-Runner.md).

### 5. Run locally

```bash
cd src/Aspire/AppHost
dotnet run
```

Use the API URL shown by Aspire to open `/swagger`. Register, log in and call an authorized endpoint. Return to the product root in another terminal for verification. The compose alternative is `docker compose up -d` after configuring `.env`.

## Verify

```bash
dotnet build Acme.slnx
dotnet test Acme.slnx                    # Docker running
git grep -i acme -- '*.slnx'             # the rename landed
bash scripts/check-docs.sh               # docs links + placeholders
dotnet test src/Api.Tests                # architecture guardrails
```

## Build your first module

```bash
dotnet new starterkit-module -n Orders   # four projects, a manifest, an example aggregate, tests, a migration
dotnet build Acme.slnx && dotnet test src/Modules/Orders/Orders.Tests
```

The host picks the module up without edits ([ADR-0019](../Decisions/0019-module-manifests-and-discovery.md)). Grow it
with `/starterkit:add-aggregate`, `/starterkit:add-feature` and `/starterkit:write-tests`. Without Claude Code, follow
the `SKILL.md` files in `tools/claude/starterkit-plugin/skills/`.

## Pitfalls

- Regenerate initial migrations only before production deployment; preserve migration history after deployment. Production runs `dotnet run --project src/Api -- --migrate-only` before starting replicas.
- If module post-actions cannot select a solution, use `dotnet sln Acme.slnx add` with the four generated project paths.
- Generated products do not receive later template fixes automatically.

## Related

[Templates](Templates.md), [Project structure](../Architecture/ProjectStructure.md), [Migrations](../Reference/Migrations.md), and `/starterkit:new-project`.
