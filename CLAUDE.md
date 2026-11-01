# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**StarterKit** is a generic, production-ready **modular monolith starter template** built with ASP.NET Core 9.
It ships the plumbing every new backend needs - identity (JWT + refresh tokens, roles, admin management), file
attachments (Local / Azure Blob / S3), subscriptions (subscribers, plans, payments), email + WhatsApp notifications,
Redis caching, Hangfire jobs, MassTransit messaging, OpenTelemetry observability, Docker/Nginx/GitHub Actions
deployment - plus a small **Catalog** sample module that is the reference vertical slice for new modules.

The public host is configured per deployment (`api.example.com` in templates / `DOMAIN` env var).

## Build & Run Commands

```bash
# Build all projects (always pass the solution file - the root may contain more than one solution file)
dotnet build StarterKit.slnx

# Run with .NET Aspire (recommended for development - orchestrates API + all infra)
cd src/Aspire/AppHost && dotnet run

# Run with Docker Compose (development - auto-merges override with dev tools)
cp .env.example .env
docker compose up -d

# Production core only
docker compose -f docker-compose.yml up -d

# With observability stack
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d
```

## Testing

```bash
# Run all tests
dotnet test StarterKit.slnx

# Run tests for a specific module
dotnet test src/Modules/Catalog/Catalog.Tests/
dotnet test src/Modules/Subscriptions/Subscriptions.Tests/
```

## Database Migrations

Each module has its own DbContext (and its own PostgreSQL schema). Use the module's context when adding migrations:

```bash
# From the repo root. --project MUST be the module that owns the DbContext (else: "target project doesn't match migrations assembly")
dotnet ef migrations add <MigrationName> \
  --context <ModuleDbContext> \
  --project src/Modules/<Module>/<Module> \
  --startup-project src/Api \
  --output-dir Data/Migrations

# Apply migrations (modules also auto-migrate on startup via UseMigration<TDbContext>())
dotnet ef database update --context <ModuleDbContext> --project src/Modules/<Module>/<Module> --startup-project src/Api
```

Shared.Email / Shared.Notification: use `--project src/Shared/Shared.Email` / `src/Shared/Shared.Notification`.

Contexts: `ApplicationIdentityDbContext`, `AttachmentDbContext`, `SubscriptionsDbContext`, `CatalogDbContext`, `EmailDbContext`, `NotificationDbContext`

All contexts share one database; the connection string name is `starterkit` (`ConnectionStrings:starterkit`).

## Development Services (Docker Compose)

| Service | URL |
|---------|-----|
| API + Swagger | http://localhost:5000 / http://localhost:5000/swagger |
| Seq (structured logs) | http://localhost:5341 |
| PgAdmin | http://localhost:5050 |
| Redis Commander | http://localhost:8081 |
| Mailhog | http://localhost:8025 |

(Host ports are configurable in `.env` - see `.env.example`.)

## Architecture

### Modular Monolith - Vertical Slice Architecture

The solution (`StarterKit.slnx`) contains independent modules under `src/Modules/`. Each module has:
- A main implementation project (own DbContext, repositories, domain models, features)
- A `.Contracts` project (commands, queries, DTOs - shared interfaces only)
- A `<Module>Module.cs` registration class (`Add<Module>Module` / `Use<Module>Module`) called from `src/Api/Program.cs`
- Optionally a `<Module>.Tests` project

Current modules: **Identity**, **Attachment**, **Subscriptions** (Subscriber / Plan / Subscription), **Catalog** (sample - Product aggregate)

Shared cross-cutting concerns live in `src/Shared/`: `Shared.Contracts` (CQRS interfaces, pagination, enums), `Shared.Core` (base classes, pipeline behaviors, EF/Redis/web helpers), `Shared.Messaging` (MassTransit config), `Shared.Email` (email sending + `EmailDbContext`), `Shared.Notification` (WhatsApp/Email notifications + `NotificationDbContext`).

### Feature Folder Structure (Vertical Slice)

Each aggregate follows this layout - use it (and `src/Modules/Catalog`) as the template for all new work:

```
Domains/<Aggregate>/
├── Models/           # Entity + child entities with factory methods
├── Features/
│   ├── Create<Aggregate>/
│   │   ├── Create<Aggregate>Handler.cs   # Validator + MediatR handler in same file
│   │   └── Create<Aggregate>EndPoint.cs  # Carter endpoint
│   ├── Update<Aggregate>/
│   ├── Delete<Aggregate>/
│   ├── Get<Aggregates>/
│   └── Get<Aggregate>ById/
├── Events/           # Domain events
├── EventHandler/
├── Exceptions/
└── Mappings/         # Mapster config
```

### Key Patterns

**Entities** use static factory methods and raise domain events. Private setters enforce invariants.

**Command handlers** contain a `*Validator : AbstractValidator<TCommand>` in the same file, before the handler class. Handlers use `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` from `Shared.Contracts`.

**Query handlers** receive the `*DbContext` directly (not a repository) for efficient projection via LINQ. All queries paginate using `PaginatedResult<T>`.

**Repositories** follow the pattern `I<Aggregate>Repository` / `<Aggregate>Repository` - one per aggregate root, injected into command handlers.

**Endpoints** use Carter (`ICarterModule`) with fluent route configuration, not controllers.

**EF Core** uses snake_case naming (`EFCore.NamingConventions`). All EF configurations live in `Data/Configurations/`.

### Startup Order (src/Api/Program.cs)

Docker secrets → Shared infrastructure → Hangfire → Rate limiting → CORS → Seq → MassTransit (module assemblies) → Enum lookup (enum assemblies) → Module registration (Identity → Attachment → Subscriptions → Catalog → Email infrastructure → Notification infrastructure) → Hangfire dashboard → Middleware pipeline (`Use<Module>Module()` in the same order)

## Strict Naming Conventions

| Type | Convention |
|------|-----------|
| Handler | `Create<X>Handler`, `Get<X>sHandler` |
| Validator | `Create<X>CommandValidator` |
| Endpoint | `Create<X>EndPoint`, `Get<X>sEndPoint` |
| Command/Query | `Create<X>Command`, `Get<X>sQuery`, `Get<X>ByIdQuery` |
| DTO | `Get<X>sDto`, `<X>Dto` |
| Result | `Create<X>Result`, `Get<X>sResult` |
| Request/Response | `Create<X>Request`, `Create<X>Response` |
| Events | `<X>CreatedEvent`, `<X>UpdatedEvent` |
| Exceptions | `<X>NotFoundException`, `Invalid<X>Exception` |
| Repository | `I<X>Repository` / `<X>Repository` |

## Hard-won Patterns

- **Entity `CreatedAt`**: the base `Aggregate<Guid>` has `CreatedAt DateTime?`. When an aggregate needs a non-nullable creation date, shadow it with `public new DateTime CreatedAt { get; private set; }`.
- **Domain events**: raise with `AddDomainEvent(...)` inside the entity. `DispatchDomainEventsInterceptor` dispatches them on `SaveChanges` via MediatR `Publish`.
- **Contracts events** (events other modules consume) live in `<Module>.Contracts` and implement `IDomainEvent` (which extends `INotification`).
- **One event, many handlers**: the same domain event may be handled by several modules (e.g. one module updates a role, another creates a profile). That is valid and preferred over direct calls.
- **Cross-module data access**: only via `ISender` + the other module's `.Contracts` commands/queries. Never reference another module's implementation project, models or DbContext.
- **`IHttpContextService`** lives in `Shared.Core.Web` and is not in every module's GlobalUsings - add `using Shared.Core.Web;` explicitly when needed.
- **Avoid `[FromServices]`**: it needs `Microsoft.AspNetCore.Mvc`, and Carter/minimal APIs resolve services from parameters automatically.
- **Roles**: `RoleConstants` exists in both `Identity` (`Domains/Roles/Constants/RoleConstants.cs`, names + seeded ids) and `Shared.Core.Authorization` (names only). Keep them in sync. Seeded role ids: User `11111111-1111-1111-1111-111111111111`, Admin `33333333-3333-3333-3333-333333333333`, SuperAdmin `44444444-4444-4444-4444-444444444444`.
- **DbContext schema**: `builder.HasDefaultSchema("<module>")` + `builder.ToSnakeCaseTables()` (+ `builder.ConfigureVersionConcurrencyTokens()`).
- **Soft delete**: do not call `FilterSoftDeletedProperties()` unless the aggregate actually needs soft delete.
- **Module registration**: a new module must be added to `AddMassTransitWithAssemblies(...)` and, if it defines enums exposed through the enum lookup endpoint, to `AddEnumLookup(...)` - not just `Add<Module>Module(...)` / `Use<Module>Module()`.
- **Workflow**: make file changes only - **never commit**. The user reviews the diff and commits.

## Key Documentation (wiki/)

When working on a specific area, consult the relevant wiki file before making changes:

| Task | Reference |
|------|-----------|
| Understanding patterns & conventions | `wiki/Architecture/CodingStandards.md`, `wiki/Architecture/DesignPatterns.md` |
| Adding a new module / sample module | `wiki/Modules/Catalog.md` |
| EF Core / database conventions | `wiki/Architecture/DatabaseConventions.md` |
| API endpoint design | `wiki/Architecture/ApiGuidelines.md` |
| Adding EF migrations | `wiki/Reference/Migrations.md` |
| Template known issues / roadmap | `wiki/Reference/Template-Backlog.md` |
| Identity / JWT / auth | `wiki/Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md` |
| File uploads / attachments | `wiki/Attachment/Implementation.md` |
| Email / notifications | `wiki/Shared/Email-Module-Usage.md`, `wiki/Shared/Notification-Module-Usage.md` |
| Redis caching | `wiki/Infrastructure/Redis.md` |
| Deployment / production | `wiki/deployment/Production-Deployment.md` |
| CI/CD pipeline | `wiki/deployment/GitHub-Actions-CI-CD.md` |
| Secrets management | `wiki/deployment/README.secrets.md` |

Full index: `wiki/INDEX.md`

## Documentation Routing

When producing a markdown document, route it to the right place before writing:

**Put it in `wiki/` (committed to the repo) when:**
- It is reference material any developer cloning the repo needs
- Examples: coding standards, API guidelines, migration commands, deployment playbooks, module architecture decisions

**Put it in your notes vault (`<your notes vault>/projects/<project>/`) when:**
- It is a flow diagram, session analysis, design review, or "how this works" explanation
- It documents *understanding* of the system rather than *specifying* it
- It is not useful to someone reading the code cold - only to the active developer
- Examples: request flow walkthroughs, before/after refactor comparisons, architectural investigation notes

**Never create a markdown file in the repo root** (only `README.md`, `CLAUDE.md` and `AGENTS.md` belong there).

When in doubt: if the doc would clutter `git diff` without adding value to a code reviewer, it goes to the notes vault.

## Code Review Workflow

When asked to review the codebase, follow this exact process:

### Engineering Preferences (apply to all recommendations)
- DRY is important - flag repetition aggressively.
- Well-tested code is non-negotiable; err toward more tests.
- "Engineered enough" - not fragile/hacky, not over-abstracted.
- Handle more edge cases, not fewer; thoughtfulness > speed.
- Explicit over clever.

### Review Sections (in order)
1. **Architecture** - system design, component boundaries, dependency graph, coupling, data flow, scaling, security (auth, data access, API boundaries).
2. **Code Quality** - organization, DRY violations, error handling gaps, technical debt, over/under engineering.
3. **Tests** - coverage gaps (unit/integration/e2e), assertion strength, missing edge cases, untested failure modes.
4. **Performance** - N+1 queries, memory usage, caching opportunities, high-complexity paths.

### Interaction Rules
- **BEFORE starting**: Ask the user: BIG CHANGE (4 issues per section, one section at a time) or SMALL CHANGE (1 issue per section).
- **Per issue**: describe the problem with file+line refs; present 2-3 lettered options (A/B/C) including "do nothing" where reasonable; for each option give effort, risk, impact, and maintenance burden; give an opinionated recommendation mapped to the engineering preferences above.
- **Number issues** (Issue 1, Issue 2…) and **letter options** (A, B, C). Recommended option is always listed first.
- Use `AskUserQuestion` after each section - do not proceed without explicit confirmation.
- Do not assume priorities on timeline or scale.

### Per-option format
For each option state: **Effort** | **Risk** | **Impact** | **Maintenance burden** - then give a clear recommendation and explain why it maps to the engineering preferences.

## Rules

- After completing any code changes, always run `dotnet build StarterKit.slnx` from the repo root and fix all errors before considering the task done.
- Never commit; leave changes in the working tree for the user to review.
- Never put real credentials, hostnames or IPs in committed files - use `CHANGE_ME`, `api.example.com`, `YOUR_SERVER_IP`.

## Optimistic Concurrency

Every entity inherits `long Version` from `Entity<T>` automatically - no action needed for new entities.

**For new critical update commands** (where concurrent edits are a real risk - e.g., profile updates, financial config, status transitions):
- Add `long? Version = null` (nullable/optional) to the command record - keeps existing frontend clients working.
- In the handler, set OriginalValue before calling SaveChangesAsync:
  ```csharp
  if (command.Version.HasValue)
  {
      dbContext.Entry(entity).Property(e => e.Version).OriginalValue = command.Version.Value;
  }
  ```
- Add `long Version` to the corresponding read DTO so clients can round-trip it.

See `UpdateProductCommand` / `ProductDto` in the Catalog sample.

**Rules:**
- Do NOT increment `Version` in entity methods or handlers - `AuditableEntityInterceptor` does it centrally on `Modified`.
- New entities start at `Version = 0` (C# default); first save leaves it at 0 (interceptor skips `Added` state).
- Delete commands: never need a version field.
- Non-critical admin-only updates (status toggles, lookup table edits): version optional - add only if there's genuine concurrency risk.
- A stale version returns HTTP 409 Conflict automatically via `ConcurrencyHandlingBehavior`.

## Infrastructure Notes

- **Secrets**: Production secrets in `./secrets/*.txt` (git-ignored, see `secrets/README.md`), mounted at `/run/secrets/` by Docker. `appsettings*.json` contain placeholders only.
- **CI/CD**: GitHub Actions (`.github/workflows/hybrid-ci-cd.yml`) - push to `master` triggers build/test → Docker image to GHCR (`ghcr.io/<owner>/<repo>-api`) → manual approval (GitHub `production` environment protection) → deploy on a self-hosted VPS runner with health check; `rollback.yml` restores the previous image/backup. Host, paths and names come from repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`, `BACKUP_BUCKET`).
- **Deployment scripts**: `deployment/deploy.sh` (zero-downtime), `deployment/rollback.sh`, `deployment/secrets-setup.sh`; production Nginx site config in `deployment/nginx/conf.d/app-production.conf`.
- **Centralized packages**: All NuGet versions in `Directory.Packages.props`; global .NET 9 settings in `Directory.Build.props`.

## Starting a New Project from StarterKit

1. Copy the folder (without `.git`, `bin/`, `obj/`) to a new location and `git init`.
2. Rename `StarterKit.slnx` to `<YourProject>.slnx` and update references in `.github/workflows/*.yml`, `Makefile`, `README.md`, `CLAUDE.md`, `AGENTS.md`.
3. Case-insensitive search/replace `starterkit` / `StarterKit` (container names, DB name/user, Redis instance name, JWT issuer/audience, `/opt/starterkit`, Grafana/pgAdmin labels). Keep the `ConnectionStrings` key consistent between `appsettings*.json`, `src/Aspire/AppHost/AppHost.cs`, docker-compose and module `AddCustomDbContext(..., "<name>")` calls.
4. `cp .env.example .env` and fill it in; create `secrets/*.txt` (see `secrets/README.md`); replace every `CHANGE_ME` placeholder; set `DOMAIN` and the Nginx `server_name` / admin IP allow-list in `deployment/nginx/conf.d/app-production.conf`.
5. If you change entities/schemas, delete the affected `Data/Migrations` folders and regenerate an `Initial<Module>` migration per DbContext.
6. Delete the Catalog sample when you no longer need it (steps in `wiki/Modules/Catalog.md`).
7. GitHub: create the `production` environment with required reviewers; add secrets (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` optional, plus anything in `wiki/deployment/GITHUB-SECRETS-SETUP.md`) and repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`, `BACKUP_BUCKET`); register the self-hosted runner with the `deploy-only` label.
8. Run `dotnet build <YourProject>.slnx` and `dotnet test <YourProject>.slnx`.
