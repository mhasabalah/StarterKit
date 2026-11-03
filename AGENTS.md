# AGENTS.md

Repo-specific context for AI coding agents (OpenCode, Codex, Copilot, etc.). It is derived from `CLAUDE.md`; if the two diverge, reconcile them before relying on stale guidance.

## Project Overview

**StarterKit** is a generic modular monolith starter template built with ASP.NET Core 10. Modules: `Identity`, `Attachment`, `Subscriptions`, and the `Catalog` sample module (Product aggregate) that serves as the reference vertical slice. The public host is configured per deployment (`api.example.com` in templates).

## Commands

```bash
# Build (always pass the solution file; the root may contain more than one)
dotnet build StarterKit.slnx

# CI-equivalent restore/build/test
dotnet restore StarterKit.slnx
dotnet build StarterKit.slnx --configuration Release --no-restore
dotnet test StarterKit.slnx --configuration Release --no-build --verbosity normal

# Run with .NET Aspire (recommended for development; orchestrates API + infra; wiki/Infrastructure/Aspire.md)
cd src/Aspire/AppHost && dotnet run
# + Grafana/Tempo/Loki/Jaeger/Prometheus stack
cd src/Aspire/AppHost && dotnet run -- Observability:FullStack=true

# Run with Docker Compose dev tools; docker-compose.override.yml is auto-merged
cp .env.example .env
docker compose up -d

# Production core only
docker compose -f docker-compose.yml up -d

# Production core + observability stack
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d
```

## Testing

```bash
dotnet test StarterKit.slnx
dotnet test src/Modules/Catalog/Catalog.Tests/
```

After completing code changes, always run `dotnet build StarterKit.slnx` from the repo root and fix errors before considering the work done. Never commit - the user reviews and commits.

## Solution Structure

- Solution file: `StarterKit.slnx`; package versions centralized in `Directory.Packages.props`; global .NET settings in `Directory.Build.props`.
- `src/Api/Api.csproj` is the API entrypoint; `src/Api/Program.cs` wires shared infrastructure and module registration.
- Modules under `src/Modules/`: `Identity` (+ `UserDirectory.Contracts`, the only user API other modules may use), `Tenancy`, `Attachment`, `Subscriptions`, `Notification` (email/WhatsApp delivery, incl. OTP codes issued by Identity), `Catalog` (sample).
- Each module has a main implementation project plus a thin `.Contracts` project (references only `Shared.Contracts`) for commands, queries, DTOs and integration events; optionally a `.Tests` project.
- Shared projects in `src/Shared/`: `Shared.Contracts`, `Shared.Core`, `Shared.Messaging`.

## Architecture Patterns

- Vertical slices under `Domains/<Aggregate>/Features/<Action>/`.
- Entities use static factory methods, private setters, and raise domain events via `AddDomainEvent()` (dispatched on SaveChanges by `DispatchDomainEventsInterceptor`) and cross-module integration events via `AddIntegrationEvent()` (transactional outbox).
- Command handlers use repositories and implement `ICommandHandler<TCommand, TResult>` from `Shared.Contracts`.
- Query handlers receive the module `*DbContext` directly for LINQ projection; queries paginate with `PaginatedResult<T>`.
- Put the `*Validator : AbstractValidator<TCommand>` in the same file as the handler, before the handler class.
- Endpoints use Carter `ICarterModule`, not controllers. Avoid `[FromServices]`.
- EF Core uses snake_case naming; EF configurations live in `Data/Configurations/`; each DbContext sets `HasDefaultSchema("<module>")` + `ToSnakeCaseTables()`.
- Repositories: `I<Aggregate>Repository` / `<Aggregate>Repository`, one per aggregate root.
- Cross-module access only through `ISender` + the other module's `.Contracts` - never reference another module's implementation.
- `RoleConstants` exists in Identity and `Shared.Core.Authorization`; keep them in sync.

## Messaging (domain vs integration events)

Full guide: `wiki/Architecture/Messaging.md` (mirrors the *Messaging* section of `CLAUDE.md`).

- Sync cross-module calls: `ISender` + `.Contracts` (no gRPC inside the monolith).
- **Integration event** = a fact other modules react to eventually; the publisher must never fail because of the reaction. If the reaction must succeed for the command to succeed, call synchronously instead. Same-module reactions = domain events.
- Add one: class in `<Module>.Contracts/<Area>/IntegrationEvents/` deriving `IntegrationEvent` with `[MessageUrn("<module>.<event-name>.v1")]` (ids + minimal projection, no `TenantId`) → raise in the aggregate with `AddIntegrationEvent(...)` → the context maps an outbox (`builder.AddOutbox()` / `builder.AddTenantOutbox()` before `ApplyTenantFilters`, migration `AddOutbox` + `EnableTenantRowLevelSecurity("<schema>", "outbox_messages")` for tenant contexts, `services.AddOutbox<TContext>()`) → `XConsumer : IConsumer<T>` in the reacting module.
- Consumers are idempotent and order-independent (re-read current state via `ISender`), and dispatch domain events of what they save (`IDomainEventDispatcher` / `IUnitOfWorkRunner`).
- Never `IPublishEndpoint.Publish` cross-module facts from feature code; never change a `[MessageUrn]` (breaking change = new `.v2` type).
- Kept in-process on purpose: `ImpersonationStartedEvent` → audit (must fail the command), `TenantDataPurgeRequestedEvent` (purge-job step).
- `Messaging:Transport` = `InMemory` (dev/tests, not durable) | `PostgreSql` | `RabbitMq`. Module `.Contracts` reference only `Shared.Contracts` (Tenancy.Contracts excepted). Enforced by `Api.Tests/Architecture/MessagingArchitectureTests`.

## Feature Folder Template

```text
Domains/<Aggregate>/
├── Models/
├── Features/
│   ├── Create<Aggregate>/
│   │   ├── Create<Aggregate>Handler.cs
│   │   └── Create<Aggregate>EndPoint.cs
│   ├── Update<Aggregate>/
│   ├── Delete<Aggregate>/
│   ├── Get<Aggregates>/
│   └── Get<Aggregate>ById/
├── Events/
├── EventHandler/
├── Exceptions/
└── Mappings/
```

## Naming Conventions

| Type | Convention |
|------|------------|
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

## Database Migrations

Each module has its own DbContext. Run EF commands from `src/Api` and always specify the context.

```bash
cd src/Api

dotnet ef migrations add <MigrationName> \
  --context <ModuleDbContext> \
  --output-dir ../Modules/<Module>/<Module>/Data/Migrations

dotnet ef database update --context <ModuleDbContext>
```

Contexts: `ApplicationIdentityDbContext`, `AttachmentDbContext`, `SubscriptionsDbContext`, `CatalogDbContext`, `TenancyDbContext`, `NotificationDbContext`.

Multi-tenancy (optional, `MultiTenancy:Enabled`): business data lives in `ITenantDataContext`s with `IMustHaveTenant`
entities, named query filters and Postgres RLS; never call the parameterless `IgnoreQueryFilters()`. Read
`wiki/MultiTenancy/Overview.md` and `wiki/MultiTenancy/Adding-a-Tenant-Aware-Module.md` before touching data access.

## Local Services

| Service | URL |
|---------|-----|
| API + Swagger | `http://localhost:5000` / `http://localhost:5000/swagger` |
| Seq | `http://localhost:5341` |
| PgAdmin | `http://localhost:5050` |
| Redis Commander | `http://localhost:8081` |
| Mailhog | `http://localhost:8025` |

## Startup and Infrastructure

- Startup order in `src/Api/Program.cs`: Docker secrets, shared infrastructure, Hangfire, rate limiting, CORS, Seq, MassTransit, enum lookup, modules (Identity, Attachment, Subscriptions, Catalog, Email, Notification), Hangfire dashboard, middleware pipeline.
- Production secrets are in git-ignored `./secrets/*.txt` and mounted at `/run/secrets/` by Docker. `appsettings*.json` hold placeholders only.
- CI workflow `.github/workflows/hybrid-ci-cd.yml`: pushes to `master`/`develop` build and test; image push + deploy run for `master` or manual dispatch. The image is `ghcr.io/<owner>/<repo>-api`; deployment settings come from repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, ...).
- Deployment scripts: `deployment/deploy.sh`, `deployment/rollback.sh`, `deployment/secrets-setup.sh`.

## Documentation Routing

- Consult `wiki/INDEX.md` before changing documented areas.
- Architecture and conventions: `wiki/Architecture/CodingStandards.md`, `wiki/Architecture/DesignPatterns.md`; new modules: `wiki/Modules/Catalog.md`.
- EF/database: `wiki/Architecture/DatabaseConventions.md`; migrations: `wiki/Reference/Migrations.md`.
- API endpoints: `wiki/Architecture/ApiGuidelines.md`.
- Identity/JWT: `wiki/Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md`; attachments: `wiki/Attachment/Implementation.md`.
- Redis: `wiki/Infrastructure/Redis.md`; local dev with Aspire: `wiki/Infrastructure/Aspire.md`; deployment: `wiki/deployment/Production-Deployment.md`; CI/CD: `wiki/deployment/GitHub-Actions-CI-CD.md`; secrets: `wiki/deployment/README.secrets.md`.
- Do not add markdown files to the repo root (only `README.md`, `CLAUDE.md`, `AGENTS.md`). Durable developer reference docs go in `wiki/`; session notes, flow diagrams and investigation writeups go in your own notes vault (`<your notes vault>/projects/<project>/`).

## Code Review Workflow

- Before a codebase review, ask whether the user wants BIG CHANGE (4 issues per section, one section at a time) or SMALL CHANGE (1 issue per section).
- Review sections in order: Architecture, Code Quality, Tests, Performance.
- For each issue, include file and line references, 2-3 lettered options including "do nothing" where reasonable, and effort/risk/impact/maintenance burden for each option.
- Engineering preferences: flag repetition aggressively, require meaningful tests, prefer explicit over clever, handle edge cases, and avoid both fragile hacks and over-abstraction.
