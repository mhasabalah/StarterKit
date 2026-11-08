---
name: new-project
description: Bootstrap a new product from the StarterKit template with `dotnet new starterkit` - name and switches, configure .env and Docker secrets, decide multi-tenancy/messaging/storage, optionally remove the Catalog sample, regenerate migrations, and prepare GitHub CI/CD (environment, secrets, variables, runner). Use for "start a new project from StarterKit", "rename the template", "set up a new app from this starter", "remove the sample module".
---

# New project from StarterKit

The canonical checklist is `wiki/Getting-Started/New-Project.md`. Read it. This skill runs that checklist with the
user, in order, and verifies each step.

## 0. Ask first (one `AskUserQuestion`, up to 4 questions)

1. Project name (PascalCase, e.g. `Acme`) and the GitHub owner of the new repository.
2. Multi-tenancy: **off** (single tenant, the default), **shared** or **dedicated** database per tenant.
3. Messaging transport for production (`PostgreSql`, no extra infra, or `RabbitMq`) and attachment storage
   (`Local`, `AzureBlob`, `S3`).
4. Keep the Catalog sample as a reference? Keep the Claude tooling, the GitHub workflows and the VPS deploy kit?

## 1. Generate (never rename in the template repo itself)

```bash
dotnet new install StarterKit.Templates              # or: dotnet new install <path-to-template-repo>
dotnet new starterkit -n <Name> -o <target-folder>   --multitenancy off|shared|dedicated --transport PostgreSql|RabbitMq --storage Local|AzureBlob|S3   --sample true|false --ai-tooling true|false --ci true|false --deploy true|false --github-owner <owner>
cd <target-folder> && git init
```

The template renames `StarterKit` / `starterkit` / `STARTERKIT` everywhere (slnx, connection-string key, containers,
JWT issuer, Redis instance, plugin, env-var prefixes), regenerates every `UserSecretsId`, applies the presets and
leaves out what was switched off. Switches: `wiki/Getting-Started/Templates.md`.

Verify right away: `dotnet build <Name>.slnx`, `dotnet test src/Api.Tests`, `bash scripts/check-docs.sh`, and
`git grep -il starterkit` (expect nothing).

`LICENSE` (MIT): keep the template's notice for reused code (MIT requires it), and ask the user which license and
copyright holder the new product uses.

Fallback without the template pack: copy the repo, rename `StarterKit.slnx`, case-sensitive replace of the three
spellings (exclude `bin/`, `obj/`, `.git/`, `Data/Migrations/`), and resolve the template's conditional markers by
hand (the template repository's Changing-The-Template page lists them). Prefer the template.

## 2. Configuration and secrets

- `cp .env.example .env`, then fill it in. Every `CHANGE_ME` must be replaced, and `.env` never gets committed.
- Create `secrets/*.txt` as listed in `secrets/README.md` (git-ignored).
- Set `DOMAIN`, and the Nginx `server_name` and admin IP allow-list in
  `deployment/nginx/conf.d/app-production.conf`.
- The step-0 presets are already applied by the template; review them in `appsettings.json`,
  `appsettings.Docker.json` and `.env.example`.

## 3. The Catalog sample

`--sample false` already left it out (with its E2E flows, the product webhook, dashboard stats and docs rows). To
remove it from an existing product later, follow `wiki/Modules/Catalog.md` → *Removing the Catalog sample*.

## 4. Migrations

The generated migrations match the model. Only if entities or schemas changed before the first deploy: delete the
affected `Data/Migrations` folders and create one `Initial<Module>` migration per DbContext with the `add-migration`
skill. Re-add RLS calls for tenant-data
contexts, including `outbox_messages`.

## 5. GitHub

- Create a `production` environment with required reviewers.
- Add secrets: `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` (optional), plus those in `wiki/Deployment/CI-CD.md`.
- Add repository variables: `APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`,
  `BACKUP_BUCKET`.
- Register the self-hosted runner with the `deploy-only` label (`wiki/Deployment/Self-Hosted-Runner.md`).

## Verify

```bash
dotnet build <Name>.slnx
dotnet test <Name>.slnx              # Docker running
git grep -il starterkit            # expect nothing
bash scripts/check-docs.sh
```

Then start with Aspire (`cd src/Aspire/AppHost && dotnet run`), open `/swagger`, register a user, and log in.
Don't commit unless the user asks; the first commit of the new repository is normally theirs.
