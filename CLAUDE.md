# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**StarterKit** is a generic, production-ready **modular monolith starter template** built with ASP.NET Core 10.
It ships the plumbing every new backend needs - identity (JWT + refresh tokens, roles, admin management), file
attachments (Local / Azure Blob / S3), subscriptions (subscribers, plans, payments), email + WhatsApp notifications,
Redis caching, Hangfire jobs, MassTransit messaging, OpenTelemetry observability, Docker/Nginx/GitHub Actions
deployment - plus a small **Catalog** sample module that is the reference vertical slice for new modules.
**Optional multi-tenancy** (`MultiTenancy:Enabled`, off by default): tenants (companies) with branch trees, global users
with per-tenant memberships/roles, shared (tenant_id + EF filters + Postgres RLS) or dedicated databases per tenant -
see `wiki/MultiTenancy/Overview.md`.

The public host is configured per deployment (`api.example.com` in templates / `DOMAIN` env var).

## Build & Run Commands

```bash
# Build all projects (always pass the solution file - the root may contain more than one solution file)
dotnet build StarterKit.slnx

# Run with .NET Aspire (recommended for development - orchestrates API + infra; wiki/Infrastructure/Aspire.md)
cd src/Aspire/AppHost && dotnet run
# + Grafana/Tempo/Loki/Jaeger/Prometheus stack
cd src/Aspire/AppHost && dotnet run -- Observability:FullStack=true

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

Contexts: `ApplicationIdentityDbContext`, `AttachmentDbContext`, `SubscriptionsDbContext`, `CatalogDbContext`, `TenancyDbContext`, `NotificationDbContext`

Tenant-data contexts (`ITenantDataContext`: Catalog, Attachment) need `migrationBuilder.EnableTenantRowLevelSecurity(schema, table)`
for every new table (see `wiki/MultiTenancy/Adding-a-Tenant-Aware-Module.md`).

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

Current modules: **Identity**, **Tenancy** (Tenant / Branch / Membership / TenantRole / Invitation / audit - control plane), **Attachment**, **Subscriptions** (Subscriber / Plan / Subscription), **Catalog** (sample - Product aggregate), **Notification** (every email + WhatsApp delivery incl. OTP codes issued by Identity, delivery log - `wiki/Modules/Notification.md`)

Shared cross-cutting concerns live in `src/Shared/`: `Shared.Contracts` (CQRS interfaces, pagination, enums, `IDomainEvent`, `IIntegrationEvent`, `[LocalizedDescription]` - the only dependency module `.Contracts` may have), `Shared.Core` (base classes, pipeline behaviors, EF/Redis/web helpers), `Shared.Messaging` (MassTransit bus + transport selection). Shared projects hold NO business state - anything with its own schema, jobs or endpoints is a module.

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

**Entities** use static factory methods and raise domain events (`AddDomainEvent`) and, for cross-module facts, integration events (`AddIntegrationEvent`). Private setters enforce invariants.

**Command handlers** contain a `*Validator : AbstractValidator<TCommand>` in the same file, before the handler class. Handlers use `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` from `Shared.Contracts`.

**Query handlers** receive the `*DbContext` directly (not a repository) for efficient projection via LINQ. All queries paginate using `PaginatedResult<T>`.

**Repositories** follow the pattern `I<Aggregate>Repository` / `<Aggregate>Repository` - one per aggregate root, injected into command handlers.

**Endpoints** use Carter (`ICarterModule`) with fluent route configuration, not controllers.

**EF Core** uses snake_case naming (`EFCore.NamingConventions`). All EF configurations live in `Data/Configurations/`.

### Startup Order (src/Api/Program.cs)

Docker secrets → Shared infrastructure → **Multi-tenancy kernel** (`AddMultiTenancy`) → Hangfire → Rate limiting → CORS → Seq → MassTransit (module assemblies) → Enum lookup (enum assemblies) → Module registration (Identity → Tenancy → Attachment → Catalog → Subscriptions → Email infrastructure → Notification infrastructure) → Hangfire dashboard → Middleware pipeline: `UseIdentityModule(a => a.UseMultiTenancy())` = authentication → **tenant resolution** → authorization, then `UseTenancyModule()` before the other `Use<Module>Module()` calls

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
| Integration events | `<X>PublishedIntegrationEvent` + `[MessageUrn("<module>.<x>-published.v1")]` |
| Consumers | `<X>Consumer` (`IConsumer<T>`) |
| Exceptions | `<X>NotFoundException`, `Invalid<X>Exception` |
| Repository | `I<X>Repository` / `<X>Repository` |

## Hard-won Patterns

- **Entity `CreatedAt`**: the base `Aggregate<Guid>` has `CreatedAt DateTime?`. When an aggregate needs a non-nullable creation date, shadow it with `public new DateTime CreatedAt { get; private set; }`.
- **Domain events**: raise with `AddDomainEvent(...)` inside the entity. `DispatchDomainEventsInterceptor` collects them on `SaveChanges`; only events of COMMITTED saves are kept (failed saves, rolled-back/uncommitted transactions and failed units of work discard theirs). `IDomainEventDispatcher.DispatchPendingAsync` publishes them via MediatR once the outermost unit of work completes - `DispatchDomainEventsBehavior` does this for MediatR commands.
- **Jobs / hosted services / orchestrators run OUTSIDE MediatR - nothing publishes their events unless you do.** Do DB work through `IUnitOfWorkRunner.RunAsync((services, ct) => ..., ct)` (`Shared.Core.Data`): a fresh scope per call, events published after success, discarded on failure. Use one call per item in batch jobs. A Hangfire job whose scoped deps Hangfire injected may instead take `IDomainEventDispatcher` and call `DispatchPendingAsync` after `SaveChanges`. Never hand-roll `IServiceScopeFactory` scopes for DB work. Enforced by `Api.Tests/Architecture/BackgroundJobEventDispatchTests`.
- **Concurrency exceptions are translated in ONE place**: DbContexts throw EF's `DbUpdateConcurrencyException` unchanged; `ConcurrencyHandlingBehavior` (MediatR) turns it into `ConcurrencyConflictException`; `ExceptionStatusMapper` maps both to 409. In jobs, log-and-skip the item with `catch (Exception ex) when (ex.IsConcurrencyConflict())` - never let one conflict abort the batch. (`SubscriptionsDbContext` separately maps Postgres 23505 to `ConflictException`.)
- **Time**: inject `TimeProvider` (registered as `TimeProvider.System`) instead of `DateTime.UtcNow` in new code; `AuditableEntityInterceptor` uses it.
- **Events other modules consume**: by default an **integration event** (`<Module>.Contracts/.../IntegrationEvents/`, outbox, async) - see *Messaging* below. A `.Contracts` `IDomainEvent` (in-process MediatR) is only for a reaction that must succeed inside the same unit of work (e.g. `ImpersonationStartedEvent` → audit).
- **One event, many handlers**: the same event may be handled/consumed by several modules (e.g. one module updates a role, another creates a profile). That is valid and preferred over direct calls.
- **Cross-module data access**: only via `ISender` + the other module's `.Contracts` commands/queries. Never reference another module's implementation project, models or DbContext.
- **Users from other modules**: only through `UserDirectory.Contracts` (`GetUserSummariesQuery`, `GetUserProfileQuery`, `GetUserContactInfoQuery`, `GetUsersInRoleQuery`, ...). `Identity.Contracts` is private to Identity and the host (architecture test) so the identity provider stays replaceable - see `wiki/Identity/Replacing-the-Identity-Provider.md`.
- **Sending email / WhatsApp**: inject `INotificationSender` (`Notification.Contracts`): `.For(userId)` or `.ToEmail(address)` → `.WithEmail(EmailTemplate.X, model.ToDictionary())` → `.SendAsync(...)`. Force `NotificationChannel.Email` for security mail. There is no direct email API.
- **`IHttpContextService`** lives in `Shared.Core.Web` and is not in every module's GlobalUsings - add `using Shared.Core.Web;` explicitly when needed.
- **Avoid `[FromServices]`**: it needs `Microsoft.AspNetCore.Mvc`, and Carter/minimal APIs resolve services from parameters automatically.
- **Roles**: `RoleConstants` exists in both `Identity` (`Domains/Roles/Constants/RoleConstants.cs`, names + seeded ids) and `Shared.Core.Authorization` (names only). Keep them in sync. Tokens carry ROLES only: `PermissionClaimsTransformation` derives `permission` claims per request from `Shared.Core.Authorization.RolePermissionMap` (never add permission claims at token issue). Seeded role ids: User `11111111-1111-1111-1111-111111111111`, Admin `33333333-3333-3333-3333-333333333333`, SuperAdmin `44444444-4444-4444-4444-444444444444`, Finance `66666666-…`, Support `77777777-…`. Admin features use fine-grained permissions (`Shared.Core/Authorization/Permissions.cs` + `.RequirePermission(...)`); privileged roles need 2FA (see `wiki/Identity/Permissions-2FA-and-Admin-Audit.md`).
- **DbContext schema**: `builder.HasDefaultSchema("<module>")` + `builder.ToSnakeCaseTables()` (+ `builder.ConfigureVersionConcurrencyTokens()`).
- **Soft delete**: do not call `FilterSoftDeletedProperties()` unless the aggregate actually needs soft delete.
- **Module registration**: a new module must be added to `AddMassTransitWithAssemblies(configuration, ...)` (its consumers) and, if it defines enums exposed through the enum lookup endpoint, to `AddEnumLookup(...)` - not just `Add<Module>Module(...)` / `Use<Module>Module()`.
- **Multi-tenancy** (`Shared.Core.MultiTenancy`, `wiki/MultiTenancy/*`):
  - Business data lives in `ITenantDataContext`s; every entity there is `IMustHaveTenant` (children too), unique indexes start with `TenantId`, `ApplyTenantFilters(builder)` is the LAST line of `OnModelCreating`. Never set `TenantId` by hand - `TenantEntityInterceptor` stamps it and blocks cross-tenant writes.
  - All query filters are NAMED (`QueryFilterNames`). Never call the parameterless `IgnoreQueryFilters()` (it drops tenant isolation; architecture test) - use `IgnoreNonIsolationFilters()` / `IgnoreBranchScope()`.
  - The ambient tenant is `ITenantContext` (AsyncLocal `TenantScope`). Cross-tenant/platform work: `using (TenantScope.BeginHostScope())`, small and explicit. Recurring jobs on tenant data: `ITenantJobRunner.ForEachTenantScopeAsync`. Hangfire jobs and MassTransit messages carry the tenant automatically.
  - Tenant authorization: `.RequireTenantPermission(TenantPermissions.X)` / `.RequireTenantMember()` (membership-based, NOT JWT claims). Platform admin endpoints: `.HostOnly().RequirePermission(Permissions.TenantsManage)`. Endpoints of tenant-data modules require a tenant automatically. Destructive/owner endpoints: also `.DisallowImpersonation()` (support impersonation tokens carry `act`).
  - Tenant moves/exports/purges are generic over every `IMustHaveTenant` table (needs `tenant_id`); a `Migrating` tenant is read-only (writes 503, jobs/messages deferred).
  - Tenant data in caches: `ITenantCacheKeys.For(key)`. Blobs: `StoragePaths.TenantRoot(tenantId)`.
  - Single-tenant mode = the Default tenant (`TenantDefaults.DefaultTenantId`) everywhere; per-tenant seed ids via `TenantSeedIds.For`.
  - In tests use `TenantContext.SingleTenant` / `TenantScope.Begin(...)`; `MultiTenantE2EFixture` boots a multi-tenant host.
- **Workflow**: make file changes only - **never commit**. The user reviews the diff and commits.

## Messaging: domain events vs integration events (outbox)

Full guide: `wiki/Architecture/Messaging.md`. Sync cross-module calls = `ISender` + `.Contracts` (no gRPC inside the monolith).

**Which one?** An **integration event** is a fact OTHER modules react to *eventually*; the publisher must never fail or
roll back because of the reaction. If the reaction must succeed for the command to succeed, it is a synchronous
`ISender` call (or, exceptionally, an in-process `.Contracts` `IDomainEvent`). Same-module reactions = domain events.

| Flow | Kind |
|---|---|
| Catalog `ProductPublishedIntegrationEvent` → any | Integration |
| Subscriptions `SubscriptionActivated/ExpiredIntegrationEvent` → Tenancy suspend/reactivate (`TenantBillingStatusConsumer`) | Integration |
| Attachment `UserAvatarChangedIntegrationEvent` → Identity `ProfilePictureUrl` (`UserAvatarChangedConsumer`) | Integration |
| `ImpersonationStartedEvent` → Tenancy audit (must fail the command) | In-process, sync |
| `TenantDataPurgeRequestedEvent` → blob/log purge (purge-job orchestration step) | In-process |

**Adding an integration event:**
1. Class in `<Module>.Contracts/<Area>/IntegrationEvents/`: `[MessageUrn("<module>.<event-name>.v1")] public sealed class XIntegrationEvent : IntegrationEvent` - ids + minimal projection, no entities, no `TenantId` (it travels as a header).
2. Raise it in the aggregate: `AddIntegrationEvent(new XIntegrationEvent { ... })` (next to the domain event).
3. Context owns an outbox: `builder.AddOutbox()` (host context) / `builder.AddTenantOutbox()` (tenant-data context, before `ApplyTenantFilters`), migration `AddOutbox` (+ `EnableTenantRowLevelSecurity("<schema>", "outbox_messages")` for tenant contexts), and `services.AddOutbox<TContext>()` in `<Module>Module.cs`.
4. Consumer `XConsumer : IConsumer<XIntegrationEvent>` in the reacting module (assembly passed to `AddMassTransitWithAssemblies`). It must be **idempotent** and **order-independent** (treat the event as a trigger, re-read current state via `ISender`), and - running outside MediatR - dispatch domain events of what it saves (`IDomainEventDispatcher.DispatchPendingAsync` / `IUnitOfWorkRunner`). Throw to retry; return for "nothing to do".

**Rules:**
- Never `IPublishEndpoint.Publish` cross-module facts from feature code: it bypasses the outbox (lost on crash, sent on rollback).
- Never change a `[MessageUrn]`; evolve by adding optional properties; breaking change = new type with `.v2` urn.
- Delivery is at-least-once (`MessageId` = `EventId`), ordering best-effort; failed rows retry with back-off and are dead-lettered after `Messaging:Outbox:MaxAttempts` (`outbox_messages.dead_lettered_at` - requeue SQL in the wiki).
- Transport: `Messaging:Transport` = `InMemory` (default; dev/tests, NOT durable) | `PostgreSql` (durable, queues in the existing DB) | `RabbitMq` (`ConnectionStrings:rabbitmq` or `Messaging:RabbitMq:*`; password via secret `rabbitmq_password`). Production should not run `InMemory`.
- Enforced by `Api.Tests/Architecture/MessagingArchitectureTests` (events in `.Contracts`, unique urns, thin contracts, consumers in modules).

## Key Documentation (wiki/)

When working on a specific area, consult the relevant wiki file before making changes:

| Task | Reference |
|------|-----------|
| Understanding patterns & conventions | `wiki/Architecture/CodingStandards.md`, `wiki/Architecture/DesignPatterns.md` |
| Adding a new module / sample module | `wiki/Modules/Catalog.md` |
| Multi-tenancy (tenants, branches, isolation, ops) | `wiki/MultiTenancy/Overview.md`, `wiki/MultiTenancy/Adding-a-Tenant-Aware-Module.md` |
| EF Core / database conventions | `wiki/Architecture/DatabaseConventions.md` |
| API endpoint design | `wiki/Architecture/ApiGuidelines.md` |
| Adding EF migrations | `wiki/Reference/Migrations.md` |
| Template known issues / roadmap | `wiki/Reference/Template-Backlog.md` |
| Identity / JWT / auth | `wiki/Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md` |
| File uploads / attachments | `wiki/Attachment/Implementation.md` |
| Email / notifications | `wiki/Modules/Notification.md` |
| One-time codes (OTP: issue, verify, limits) | `wiki/Identity/README.md#one-time-codes-otp` (Identity `IOtpCodeService`) |
| Domain vs integration events, outbox, transports | `wiki/Architecture/Messaging.md` |
| Swapping the identity provider (Keycloak, ...) | `wiki/Identity/Replacing-the-Identity-Provider.md` |
| Redis caching | `wiki/Infrastructure/Redis.md` |
| Local dev with Aspire (AppHost, dashboard, observability stack) | `wiki/Infrastructure/Aspire.md` |
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
- In the handler, after loading the tracked entity and before SaveChangesAsync, call the shared helper
  (`Shared.Core.Data.DbContextVersioningExtensions`). It handles `null` by keeping the loaded version:
  ```csharp
  dbContext.ExpectVersion(entity, command.Version);
  ```
- Add `long Version` to the corresponding read DTO so clients can round-trip it.

See `UpdateProductCommand` / `ProductDto` in the Catalog sample.

**How it works.** EF emits `UPDATE … WHERE version = @OriginalValue`. `ExpectVersion` puts the client's version into
`OriginalValue`. `AuditableEntityInterceptor` then sets `CurrentValue = OriginalValue + 1` and must never overwrite `OriginalValue`.
An older version of the interceptor did overwrite it, which silently disabled stale-version detection. That is now guarded by
`Shared.Core.Tests/Concurrency/*` (Testcontainers Postgres).

**Rules:**
- Do NOT set `Version` or `OriginalValue` by hand. Do NOT increment `Version` in entity methods or handlers: `AuditableEntityInterceptor` does it centrally on `Modified`, and also when only an owned entity changed.
- New entities start at `Version = 0` (C# default); first save leaves it at 0 (interceptor skips `Added` state).
- Delete commands: never need a version field.
- Non-critical admin-only updates (status toggles, lookup table edits): version optional - add only if there's genuine concurrency risk.
- A stale version returns HTTP 409 Conflict automatically via `ConcurrencyHandlingBehavior`.

## Infrastructure Notes

- **Secrets**: Production secrets in `./secrets/*.txt` (git-ignored, see `secrets/README.md`), mounted at `/run/secrets/` by Docker. `appsettings*.json` contain placeholders only.
- **CI/CD**: GitHub Actions (`.github/workflows/hybrid-ci-cd.yml`) - push to `master` triggers build/test → Docker image to GHCR (`ghcr.io/<owner>/<repo>-api`) → manual approval (GitHub `production` environment protection) → deploy on a self-hosted VPS runner with health check; `rollback.yml` restores the previous image/backup. Host, paths and names come from repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`, `BACKUP_BUCKET`).
- **Deployment scripts**: `deployment/deploy.sh` (zero-downtime), `deployment/rollback.sh`, `deployment/secrets-setup.sh`; production Nginx site config in `deployment/nginx/conf.d/app-production.conf`.
- **Centralized packages**: All NuGet versions in `Directory.Packages.props`; global .NET 10 settings in `Directory.Build.props`.

## Starting a New Project from StarterKit

1. Copy the folder (without `.git`, `bin/`, `obj/`) to a new location and `git init`.
2. Rename `StarterKit.slnx` to `<YourProject>.slnx` and update references in `.github/workflows/*.yml`, `Makefile`, `README.md`, `CLAUDE.md`, `AGENTS.md`.
3. Case-insensitive search/replace `starterkit` / `StarterKit` (container names, DB name/user, Redis instance name, JWT issuer/audience, `/opt/starterkit`, Grafana/pgAdmin labels). Keep the `ConnectionStrings` key consistent between `appsettings*.json`, `src/Aspire/AppHost/AppHost.cs`, docker-compose and module `AddCustomDbContext(..., "<name>")` calls.
4. `cp .env.example .env` and fill it in; create `secrets/*.txt` (see `secrets/README.md`); replace every `CHANGE_ME` placeholder; set `DOMAIN` and the Nginx `server_name` / admin IP allow-list in `deployment/nginx/conf.d/app-production.conf`.
5. If you change entities/schemas, delete the affected `Data/Migrations` folders and regenerate an `Initial<Module>` migration per DbContext.
6. Delete the Catalog sample when you no longer need it (steps in `wiki/Modules/Catalog.md`).
7. GitHub: create the `production` environment with required reviewers; add secrets (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` optional, plus anything in `wiki/deployment/GITHUB-SECRETS-SETUP.md`) and repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`, `BACKUP_BUCKET`); register the self-hosted runner with the `deploy-only` label.
8. Run `dotnet build <YourProject>.slnx` and `dotnet test <YourProject>.slnx`.
