# CLAUDE.md

This file guides Claude Code (and, through `AGENTS.md`, other agents) in this repository. It holds **rules**.
Procedures live in the StarterKit plugin skills (`/starterkit:*`, see [Skills](#skills-how-to-do-things)), and
explanations live in `wiki/`.

## Project

**StarterKit** is a production-ready **modular monolith starter template** on ASP.NET Core 10. It ships:
- identity: JWT + rotating refresh tokens, roles + fine-grained permissions, 2FA, admin management, impersonation
- optional multi-tenancy (tenants, branches, memberships, shared or dedicated DBs)
- attachments (Local / Azure Blob / S3)
- subscriptions and billing
- email + WhatsApp notifications
- Redis, Hangfire, MassTransit with a transactional outbox, OpenTelemetry, .NET Aspire
- Docker/Nginx/GitHub Actions deployment

The **Catalog** module is the reference vertical slice for all new work. The public host is per deployment
(`api.example.com` in templates).

## Commands

```bash
dotnet build StarterKit.slnx                         # always pass the .slnx (the root may hold more than one solution)
dotnet test StarterKit.slnx                          # all tests - integration/E2E need Docker running
dotnet test src/Api.Tests                            # architecture guardrails only (fast, no Docker)
dotnet test src/Modules/Catalog/Catalog.Tests --filter "FullyQualifiedName~CreateProductHandlerTests"   # one class/test

cd src/Aspire/AppHost && dotnet run                  # dev: API + Postgres + Redis + Seq (Aspire dashboard)
cd src/Aspire/AppHost && dotnet run -- Observability:FullStack=true   # + Grafana/Tempo/Loki/Jaeger/Prometheus
cp .env.example .env && docker compose up -d         # dev via compose (override with dev tools merged automatically)
make help                                            # docker/db/cache/nginx shortcuts

dotnet run --project src/Api -- --migrate-only       # apply all migrations + seeds (shared + dedicated tenant DBs), then exit

# Migrations: from the repo root; --project MUST be the module owning the context
dotnet ef migrations add <Name> --context <Ctx> --project src/Modules/<M>/<M> --startup-project src/Api --output-dir Data/Migrations
```

The SDK is pinned in `global.json` (10.0.x). All NuGet versions are in `Directory.Packages.props` (never put
`Version=` on a PackageReference); global settings are in `Directory.Build.props`. The API serves `/swagger` and
`/scalar`. Ports are in `.env` (see `README.md`).

## Project map

| Path | What |
|---|---|
| `src/Api` | Host: `Program.cs`, appsettings, Dockerfile, host-level endpoints |
| `src/Modules/<M>/<M>`, `<M>.Contracts` | Module implementation + its public contracts |
| `src/Modules/<M>/<M>.Tests`, `<M>.IntegrationTests` | Unit tests; integration tests on Testcontainers Postgres (Catalog, Identity, Subscriptions, Tenancy, Webhooks; Attachment's live in `Attachment.Tests/Integration`) |
| `src/Modules/Identity/UserDirectory.Contracts` | Provider-neutral user port: the ONLY way other modules see users |
| `src/Shared/Shared.Contracts` | CQRS interfaces + the owned mediator port (`Mediation`: `ISender`, `IPublisher`, ...), pagination, `IDomainEvent`, `IntegrationEvent`, `LocalizedText`, `[LocalizedDescription]` |
| `src/Shared/Shared.Core` | Base classes, mediator behaviors, EF/outbox/multi-tenancy/auth/web helpers (`Shared.Core.Tests` beside it) |
| `src/Shared/Shared.Messaging` | MassTransit bus + transport selection |
| `src/Api.Tests` | **Architecture tests** (guardrails below) |
| `src/Api.E2ETests` | HTTP end-to-end tests (`E2ETestFixture`, `MultiTenantE2EFixture`, `PendingModelChangesTests`) |
| `src/Aspire/AppHost`, `ServiceDefaults`, `AppHost.Tests` | Local orchestration |
| `tests/e2e` | Playwright browser smoke tests (TypeScript) |
| `deployment/`, `infra/`, `.github/workflows/` | Deploy scripts, Nginx, observability configs, CI/CD |
| `tools/claude/starterkit-plugin` | This repo's Claude Code plugin (skills, reviewer agent, hooks) |

**Modules:**
- **Identity**: auth, users, roles, OTP, 2FA.
- **Tenancy**: the control plane: tenants, branches, memberships, tenant roles, invitations, audit.
- **Attachment**: files.
- **Subscriptions**: plans, subscriptions, ledger, invoices.
- **Catalog**: the sample; products, categories, tags, wishlist.
- **Notification**: every email/WhatsApp delivery and its log.
- **Webhooks**: outbound customer webhooks (tenant endpoints, signed deliveries, retries, delivery log).

Shared projects hold **no** business state. Anything with its own schema, jobs or endpoints is a module.

## Architecture rules

**Boundaries:**
- Modules call each other only through `ISender` + the other module's `.Contracts`, or through integration events.
  Never reference another module's implementation, models or DbContext.
- Users come from `UserDirectory.Contracts` only. `Identity.Contracts` is private to Identity and the host, so the
  identity provider stays replaceable.
- `.Contracts` depend on `Shared.Contracts` only. `Tenancy.Contracts → Shared.Core` is the single whitelisted
  exception.
- There is no gRPC or HTTP between modules.

**Slice layout** (copy `src/Modules/Catalog`):

```
Domains/<Aggregate>/
├── Models/            entity + children, static factories, private setters, invariants, events
├── Features/<Feature>/<Feature>Handler.cs   validator (first) + handler in one file
│                      <Feature>EndPoint.cs  Carter ICarterModule
├── Events/  EventHandlers/  Exceptions/  Consumers/  Jobs/  Services/  Mappings/
```

Small lookups may group handlers in one `<X>Handlers.cs` and their routes in a Carter `<X>sModule` (see
`Catalog/Domains/Tags`). EF configurations live in `Data/Configurations/`, migrations in `Data/Migrations/`.
Notification is the exception: its context is in `Persistence/`.

**Handlers:**
- Command handlers (`ICommandHandler<TCmd,TRes>`) use **repositories** (`I<X>Repository`, one per aggregate root)
  and aggregate methods.
- Query handlers (`IQueryHandler`) use the **DbContext** directly: `AsNoTracking`, projection in SQL, and
  `PaginatedResult<T>` for lists. Small bounded lookups may return `List<T>`.

**EF:**
- snake_case naming. `builder.HasDefaultSchema("<module>")`, then `ToSnakeCaseTables()`, then
  `ConfigureVersionConcurrencyTokens()`.
- Enums are stored as strings.
- Call `FilterSoftDeletedProperties()` only when an aggregate really soft-deletes.

**Registration:**
- `Add<M>Module` / `Use<M>Module` in `<M>Module.cs`.
- Add the module's assembly to `AddMassTransitWithAssemblies(...)`, its Contracts to `AddEnumLookup(...)` (if it has
  enums), and its name to `ModuleBoundaryTests.Modules`.

**Startup order** (`src/Api/Program.cs`):
1. Service registration:
   1. `AddDockerSecrets` → `AddSharedInfrastructure` (auth policies, CORS, localization, paging, ...)
   2. `AddMultiTenancy` → Hangfire → rate limiting → Seq → `AddMassTransitWithAssemblies` → `AddEnumLookup`
   3. Modules: Identity → Tenancy → Attachment → Catalog → Subscriptions → Webhooks → Notification
2. Middleware:
   1. `UseTrustedForwardedHeaders` (first) → `UseConfigurableCors` → `UseErrorHandling` (outside auth, so 401/403
      get JSON)
   2. `UseIdentityModule(afterAuthentication: a => a.UseMultiTenancy())` = authentication → tenant resolution →
      authorization
   3. `UseRateLimiter` (after auth, so partitions are per user) → Hangfire dashboard
   4. `UseTenancyModule()` first (the Default tenant must exist), then Attachment → Catalog → Subscriptions →
      Webhooks → Notification
   5. `UseOutboxSweep()` → `UseSharedInfrastructure()`

## Naming (enforced by `NamingConventionTests`)

| Type | Convention |
|---|---|
| Command / Query | `Create<X>Command`, `Get<X>sQuery`, `Get<X>ByIdQuery` |
| Validator / Handler | `Create<X>CommandValidator` / `Create<X>Handler` (never in Contracts) |
| Endpoint | `Create<X>EndPoint` (capital **P**) or grouped Carter `<X>sModule` |
| Result / Request / Response | `Create<X>Result`, `Create<X>Request`, `Create<X>Response` |
| DTO | `<X>Dto`, `Get<X>sDto` |
| Domain event / integration event | `<X>CreatedEvent` / `<X>PublishedIntegrationEvent` + `[MessageUrn("<module>.<x>-published.v1")]` |
| Consumer / Job | `<X>Consumer` (`IConsumer<T>`) / `<Verb><X>sJob` |
| Exceptions | `<X>NotFoundException`, `Invalid<X>Exception`, `<X>AlreadyExistsException` |
| Repository | `I<X>Repository` / `<X>Repository` |
| Tests | `Scenario_Expectation`, e.g. `Update_WithStaleVersion_IsRejected_AndNothingChanges` |

## Hard-won patterns

- **Errors.** There is no Result type. Throw the typed exceptions in `Shared.Core/Exceptions` (`NotFound`,
  `BadRequest`, `Conflict`, `Forbidden`, `Unauthorized`, `Gone`, `Domain`) or a module subclass.
  `GlobalExceptionHandler` + `ExceptionStatusMapper`, the only exception handler, turn them into the `ApiResponse` error
  envelope (`isSuccess=false, statusCode, errors[], traceId, code?`; 5xx gets a generic message and the full exception
  is logged). Successful responses use `ApiResponseExtensions.Success(...)`. A `CustomException.ErrorCode` string
  (e.g. `feature_not_in_plan`) is returned as `errorCode` for clients to branch on.
- **Idempotency and plan entitlements** (opt-in endpoint filters in Shared.Core):
  - `.WithIdempotency()` on money-moving POSTs honors an optional `Idempotency-Key` header (replay, 409 in flight,
    422 on a reused key with a different body; fails open when Redis is down).
  - `.RequireFeature("key")` checks `IEntitlementService` (Subscriptions implements it from the active plan; enforced
    only when `Subscriptions:Entitlements:Enforce=true`).
  - `.RequireQuota("key", qty)` consumes a metered plan quota (`IQuotaService`; 429 + `quota_exceeded`; fails open
    when the counter store is down).
  - `.RequireFlag("key")` gates on a runtime feature flag (`IFeatureFlags`, stored in Tenancy; 404 when off). Plan
    features are entitlements, not flags.
- **Migrations run at deploy, not boot, in production.** `Database:MigrateOnStartup` (true in dev/tests, false in
  Docker production); `--migrate-only` applies them under a Postgres advisory lock, optionally with
  `Database:MigrationConnectionString`. Never add migrate/seed work elsewhere in startup.
- **Mediator pipeline.** The mediator is OURS (`Shared.Contracts.Mediation`, adapter `Shared.Core.Mediation.Mediator`,
  registered by `AddMediator(assembly)`); never reference MediatR or another mediator package
  ([ADR-0007](wiki/Decisions/0007-owned-mediator-port.md)). It runs `RequestTelemetryBehavior` (span + duration metric; host only) → `ValidationBehavior` →
  `ConcurrencyHandlingBehavior` → `LoggingBehavior` → `DispatchDomainEventsBehavior`. Validators are discovered automatically; don't validate by hand in handlers.
- **Domain events.** Raise them with `AddDomainEvent(...)` in the entity. `DispatchDomainEventsInterceptor` keeps only
  the events of COMMITTED saves; `IDomainEventDispatcher.DispatchPendingAsync` publishes them via `IPublisher` after the
  outermost unit of work.
- **Outside the mediator** (jobs, hosted services, orchestrators, consumers), nothing publishes events unless you do.
  - Do DB work via `IUnitOfWorkRunner.RunAsync((services, ct) => ..., ct)`, one call per item. Alternatively, call
    `IDomainEventDispatcher.DispatchPendingAsync` after `SaveChanges`.
  - Never hand-roll `IServiceScopeFactory` scopes for DB work.
  - Tenant data in recurring jobs goes through `ITenantJobRunner.ForEachTenantScopeAsync`.
- **Concurrency is translated in ONE place.** DbContexts throw `DbUpdateConcurrencyException`;
  `ConcurrencyHandlingBehavior` turns it into `ConcurrencyConflictException`, which maps to 409. In jobs, log and
  skip with `catch (Exception ex) when (ex.IsConcurrencyConflict())`. `SubscriptionsDbContext` also maps Postgres
  23505 to `ConflictException`.
- **Optimistic concurrency on critical updates** (profile, financial config, status transitions):
  - Add `long? Version = null` to the command and request.
  - Call `dbContext.ExpectVersion(entity, command.Version)` after loading and before saving.
  - Expose `long Version` on the read DTO.
  - **Never** set or increment `Version`/`OriginalValue` by hand; `AuditableEntityInterceptor` does it, guarded by
    `Shared.Core.Tests/Concurrency`.
  - Deletes never take a version. Reference: `UpdateProductCommand` / `ProductDto`.
- **Time.** New code injects `TimeProvider` instead of using `DateTime.UtcNow`.
- **`CreatedAt`.** `Entity<T>.CreatedAt` is `DateTime?`. For a non-nullable one, shadow it with
  `public new DateTime CreatedAt { get; private set; }`.
- **Auth is explicit.** Carter endpoints are deny-by-default (`RequireAuthorizationByDefault()` on the Carter group
  adds `RequireAuthorization()` when an endpoint declares nothing), but that is only a safety net: the architecture
  test `Every_endpoint_declares_authorization_explicitly` fails unless every endpoint
  declares one of:
  - `.RequireAuthorization()` or `"PageGate"` (public page 1)
  - `.RequirePermission(Permissions.X)` (platform staff)
  - `.RequireTenantPermission(TenantPermissions.X)` or `.RequireTenantMember()` (membership-based, not JWT)
  - `.HostOnly().RequirePermission(Permissions.TenantsManage)`
  - an explicit `.AllowAnonymous()`

  Destructive or owner actions also get `.DisallowImpersonation()`. Security-sensitive self-service endpoints
  (password, 2FA, sessions, API keys, account deletion) also get `.DisallowApiKey()`.
- **Roles and permissions.**
  - Callers authenticate with a JWT (Bearer) or an API key (`ApiKey` scheme behind a policy scheme). A key's
    permissions are its owner's permissions intersected with the key scopes; never let a key exceed its owner.
  - Access tokens carry `sid` (the refresh-token family) so one session can be revoked immediately.
  - Tokens carry ROLES only. `PermissionClaimsTransformation` derives permissions per request from
    `RolePermissionMap`; never add permission claims at token issue.
  - `RoleConstants` exists in Identity (names + seeded ids) and in `Shared.Core.Authorization` (names); keep them in
    sync.
  - Seeded role ids: User `1111…`, Admin `3333…`, SuperAdmin `4444…`, Finance `6666…`, Support `7777…`.
  - Privileged roles need 2FA (`wiki/Identity/Permissions-2FA-and-Admin-Audit.md`).
- **Notifications.** Inject `INotificationSender`:
  `.For(userId)` / `.ToEmail(addr)` → `.WithEmail(EmailTemplate.X, model.ToDictionary())` → `.SendAsync(...)`.
  Security mail forces `NotificationChannel.Email`. There is no other email API.
- **Localization.**
  - User-facing text and validation messages use `IStringLocalizer<<M>Resource>`, with every key in both the `en`
    and `ar` resx files.
  - Bilingual data is `LocalizedText` (`name_en`/`name_ar` columns).
  - Enums exposed to clients get `[LocalizedDescription]` and are served by the enum lookup endpoint.
- **Audit trail.** Entities that need "who changed what" implement `IAuditable`; their DbContext maps
  `AddAuditTrail()` / `AddTenantAuditTrail()` (RLS in the migration for tenant data). `AuditTrailInterceptor` writes
  `audit_entries` in the same `SaveChanges`. Mark secrets and noisy fields `[AuditIgnore]`. Guarded by
  `AuditTrailArchitectureTests`.
- **Caching.** Use Redis via `IDistributedCache`. Build tenant data keys with `ITenantCacheKeys.For(key)`.
  Invalidate explicitly from domain event handlers (see `Catalog/Domains/Categories/Cache/CategoryTreeCache.cs`).
  Cache failures must never fail the request.
- **Web.** `IHttpContextService` is in `Shared.Core.Web`; add the `using` explicitly. Avoid `[FromServices]`; Carter
  resolves parameters.
- **Multi-tenancy** (`Shared.Core.MultiTenancy`, `wiki/MultiTenancy/*`):
  - **Data rules.** Business data lives in `ITenantDataContext`s (today: Catalog, Attachment).
    - Every entity there, children included, is `IMustHaveTenant`.
    - Unique indexes start with `TenantId`.
    - `ApplyTenantFilters(builder)` is the LAST line of `OnModelCreating`.
    - Every new table gets `EnableTenantRowLevelSecurity` in its migration.
    - Never set `TenantId`; `TenantEntityInterceptor` stamps it and blocks cross-tenant writes.
    - Production should run `MultiTenancy:Database:RlsBypass=Role` (host scope connects as a `BYPASSRLS` role; the
      runtime role can't lift RLS with `app.bypass_rls`; startup fails if the `tenant_rls_bypass` group role is
      missing). Tenant-data connections must never go through PgBouncer transaction pooling.
    - Tenant/membership L1 caches broadcast evictions over Redis pub/sub so every replica drops them.
  - **Filters.** All query filters are named. Never call the parameterless `IgnoreQueryFilters()` in production
    code; use `IgnoreNonIsolationFilters()` or `IgnoreBranchScope()`.
  - **Ambient tenant.** The tenant is `ITenantContext` (AsyncLocal `TenantScope`).
    - For cross-tenant work, use `using (TenantScope.BeginHostScope())`, kept small and explicit.
    - Hangfire jobs and MassTransit messages carry the tenant automatically.
    - A `Migrating` tenant is read-only: writes return 503, and jobs and messages are deferred.
  - **Blobs and seed ids.** Blob paths use `StoragePaths.TenantRoot(tenantId)`. Single-tenant mode means the Default
    tenant (`TenantDefaults.DefaultTenantId`) everywhere. Per-tenant seed ids come from `TenantSeedIds.For`.

## Messaging: domain vs integration events

The full guide is `wiki/Architecture/Messaging.md`.

**Choosing the mechanism:**
- **Same-module reaction:** domain event.
- **Another module reacts eventually, and the publisher must never fail because of it:** integration event, through
  the outbox.
- **Reaction must succeed for the command to succeed:** synchronous `ISender` call, or exceptionally an in-process
  `.Contracts` `IDomainEvent`.

| Flow | Kind |
|---|---|
| Catalog `ProductPublishedIntegrationEvent` → any | Integration |
| Subscriptions `SubscriptionActivated/ExpiredIntegrationEvent` → Tenancy `TenantBillingStatusConsumer` | Integration |
| Attachment `UserAvatarChangedIntegrationEvent` → Identity `UserAvatarChangedConsumer` | Integration |
| Identity `UserDeletedIntegrationEvent` (in `UserDirectory.Contracts`) → Attachment / Tenancy / Notification erasure | Integration |
| Subscriptions payment webhook → settlement (inbox `inbound_webhook_events`, unique per provider event) | Inbound, idempotent |
| `ImpersonationStartedEvent` → Tenancy audit (must fail the command) | In-process, sync |
| `TenantDataPurgeRequestedEvent` → blob/log purge (purge-job step) | In-process |

**Rules:**
- Mark an integration event `[PublicWebhookEvent]` to expose it to customer webhooks; add its closed
  `WebhookForwardingConsumer<T>` subclass in the Webhooks module (a unit test enforces it).
- Raise integration events with `AddIntegrationEvent` on the aggregate. **Never** call `IPublishEndpoint.Publish`
  from feature code: it bypasses the outbox, so the event is lost on a crash and sent on a rollback.
- Events live in `<M>.Contracts/<Area>/IntegrationEvents/`. They carry ids and a minimal projection, no entities and
  no `TenantId` (it travels as a header).
- A `[MessageUrn]` never changes. Evolve an event by adding optional properties; a breaking change is a new `.v2`
  type.
- Consumers are idempotent and order-independent: they re-read the current state through `ISender`. Throw to retry;
  return for "nothing to do".
- Delivery is at-least-once (`MessageId` = `EventId`). Failed rows are dead-lettered after
  `Messaging:Outbox:MaxAttempts`; inspect/requeue them via `/api/v1/admin/outbox/{context}/dead-letters`
  (`outbox.manage`, SuperAdmin). Gauges `outbox.messages.pending` / `.dead_lettered` feed the Prometheus alerts.
- `Messaging:Transport` is `InMemory` (dev/tests, NOT durable), `PostgreSql` or `RabbitMq`. Production must not run
  `InMemory`; `ProductionConfigurationGuard` refuses to boot with it.
- Bus behavior is pinned by `Shared.Core.Tests/Messaging/TenantMessagingBusTests`, which runs on InMemory AND
  PostgreSql: tenant propagation, purged-tenant drop, migrating-tenant redelivery, no noisy-neighbor starvation. Never
  remove `WaitUntilStarted` (lost messages at startup) or swap the migrating-tenant delayed redelivery back to an
  in-memory retry (starves other tenants). Messages that fail every retry go to `<endpoint>_error`; the
  `MessageConsumerFaults` alert reports them.

## Architecture guardrails (`src/Api.Tests/Architecture/`; run `dotnet test src/Api.Tests`)

| Test | Fails when |
|---|---|
| `ModuleBoundaryTests` | A module references another module's implementation; Contracts reference implementations; Shared references a module; a non-Identity module uses `Identity.Contracts`. **Register new modules in `Modules`.** |
| `NamingConventionTests` | Handler/Command/Query/Validator/Event/EndPoint/Module suffixes are wrong, or handlers sit in Contracts |
| `ThirdPartyIsolationTests` | MediatR or ImageSharp is referenced; SkiaSharp leaves Attachment (image adapter) or QuestPDF leaves Subscriptions (licenses: `wiki/Reference/Third-Party-Licenses.md`); a Contracts project references the bus (only `MassTransit.Abstractions` for `[MessageUrn]`) |
| `MessagingArchitectureTests` | Events are outside `.Contracts/.../IntegrationEvents`, have a bad or duplicate URN, or are also mediator notifications; consumers sit outside modules; outbox contexts and registrations mismatch; Contracts reference Shared.Core/Messaging. **Register new events in the expected list.** |
| `BackgroundJobEventDispatchTests` | A job, orchestrator, hosted service or consumer saves without `IUnitOfWorkRunner`/`IDomainEventDispatcher` |
| `TenantIsolationArchitectureTests` | A tenant entity lacks `IMustHaveTenant`; a query filter is unnamed; a unique index lacks `tenant_id`; parameterless `IgnoreQueryFilters()` appears |
| `AdminEndpointAuthorizationTests` | An `/admin` or `/superadmin` route has no admin or permission policy; a used permission policy is unregistered; **any endpoint declares no authorization at all** |
| `ClientGeneratedKeyTests` | A child entity (reached through a collection navigation) has a store-generated Guid key: needs `ValueGeneratedNever()` |
| `PendingModelChangesTests` (`Api.E2ETests`) | The EF model changed without a migration |
| `TenantRowLevelSecurityCoverageTests` (`Api.E2ETests`) | A table with `tenant_id` in a tenant-data schema lacks forced RLS + the `tenant_isolation` policy |

## Testing

- **Stack:** xUnit 2 + FluentAssertions 7 + Moq 4, plus Testcontainers (`postgres:16-alpine`) for integration and E2E
  tests. Don't introduce other assertion or mocking libraries.
- **Levels:**
  - domain, handler and validator unit tests (`<M>.Tests`)
  - integration on real Postgres through the real `Add<M>Module` (`<M>.IntegrationTests`)
  - HTTP E2E (`Api.E2ETests`, `CreateClientAs(userId, roles)`)
  - multi-tenant E2E (`MultiTenantE2EFixture`)
  - architecture tests
  - Playwright smoke (`tests/e2e`)
- Every failure mode gets a test: not found, forbidden, conflict, validation, stale version, cross-tenant access.
- In unit tests, use `TenantContext.SingleTenant` / `TenantScope.Begin(...)`. Tests may use `IgnoreQueryFilters()`
  to inspect data.

## Skills: how to do things

The StarterKit plugin (`tools/claude/starterkit-plugin`, enabled by `.claude/settings.json`) packages every recurring
change. **Use the matching skill instead of improvising.**

| Task | Skill |
|---|---|
| New module / bounded context | `/starterkit:new-module` |
| New entity / aggregate / table | `/starterkit:add-aggregate` |
| New endpoint / command / query / CRUD | `/starterkit:add-feature` |
| Schema change / migration | `/starterkit:add-migration` |
| Cross-module event + consumer | `/starterkit:add-integration-event` |
| Scheduled / background job | `/starterkit:add-background-job` |
| Permission / endpoint authorization | `/starterkit:add-permission` |
| Email / WhatsApp message | `/starterkit:add-email-template` |
| Tests at any level | `/starterkit:write-tests` |
| Code review (interactive, sectioned) | `/starterkit:review` (the `starterkit:architecture-reviewer` agent does the guardrail pass) |
| New product from the template | `/starterkit:new-project` |
| Documentation | `/starterkit:update-docs` |

The plugin's hooks block `git commit`/`git push` and warn on forbidden C# patterns after edits. Spec Kit
(`/speckit-*`) is available for spec → plan → tasks flows; its principles are in `.specify/memory/constitution.md`.

## Documentation

- The index of all docs is `wiki/INDEX.md`. Read the relevant page before changing an area: Architecture,
  MultiTenancy, Messaging, Modules, Identity, Attachment, Testing, Deployment, Decisions (ADRs).
- **Routing:** reference material every developer needs goes in `wiki/`. Flow walkthroughs, session analyses and
  investigations go in your notes vault (`<vault>/projects/<project>/`). Never add markdown to the repo root (only
  `README.md`, `CLAUDE.md`, `AGENTS.md`).
- When behaviour or structure changes, update the wiki page, `wiki/INDEX.md`, `README.md` and this file in the same
  change. Architectural decisions get an ADR in `wiki/Decisions/`. Check links with `bash scripts/check-docs.sh`.

## Rules

- After any code change, run `dotnet build StarterKit.slnx` from the repo root and fix every error, then run the
  relevant tests and `dotnet test src/Api.Tests`.
- **Never commit or push.** Leave changes in the working tree for the user to review.
- Never put real credentials, hostnames or IPs in committed files. Use `CHANGE_ME`, `api.example.com`,
  `YOUR_SERVER_IP`. Secrets come from `./secrets/*.txt` (Docker secrets, git-ignored) or environment variables.
- CI/CD:
  - `.github/workflows/hybrid-ci-cd.yml` runs build and test on pushes to `master`/`develop` and on PRs.
  - Pushes to `master` additionally build the GHCR image, wait for manual approval (the `production`
    environment), then deploy on the self-hosted runner.
  - `rollback.yml` restores the previous image or backup.
- Starting a new product from this template: `wiki/Getting-Started/New-Project.md` or `/starterkit:new-project`.
