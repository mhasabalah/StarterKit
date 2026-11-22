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
- bulk CSV/Excel import/export jobs (dry run, error report, resumable, worker replicas)
- notifications: email + WhatsApp (fallback chain), an in-app inbox with real-time SignalR events, FCM push
- Redis, Hangfire, MassTransit with a transactional outbox, OpenTelemetry, .NET Aspire
- Docker/Nginx/GitHub Actions deployment

<!--#if (sample)-->
The **Catalog** module is the reference vertical slice for all new work.
<!--#else-->
New modules start from `dotnet new starterkit-module` (its example aggregate is the reference slice).
<!--#endif-->
The public host is per deployment (`api.example.com` in templates).

## Commands

```bash
dotnet build StarterKit.slnx                         # always pass the .slnx (the root may hold more than one solution)
dotnet test StarterKit.slnx                          # all tests - integration/E2E need Docker running
dotnet test src/Api.Tests                            # architecture guardrails only (fast, no Docker)
dotnet test src/Modules/Identity/Identity.Tests --filter "FullyQualifiedName~ApiKeyTests"   # one class/test

cd src/Aspire/AppHost && dotnet run                  # dev: API + Postgres + Redis + Seq (Aspire dashboard)
cd src/Aspire/AppHost && dotnet run -- Observability:FullStack=true   # + Grafana/Tempo/Loki/Jaeger/Prometheus
cp .env.example .env && docker compose up -d         # dev via compose (override with dev tools merged automatically)
make help                                            # docker/db/cache/nginx shortcuts
make loadtest                                        # k6 HTTP load scenarios against a LoadTest-profile Docker stack (wiki/Testing/Load-Testing.md)

dotnet run --project src/Api -- --migrate-only       # apply all migrations + seeds (shared + dedicated tenant DBs), then exit

# Migrations: from the repo root (`dotnet tool restore` once: dotnet-ef is a local tool); --project MUST be the module owning the context
dotnet ef migrations add <Name> --context <Ctx> --project src/Modules/<M>/<M> --startup-project src/Api --output-dir Data/Migrations

dotnet new starterkit-module -n Orders               # a drop-in module (four projects, manifest, example, migration)
```
<!--#if (templateSource)-->

This repository is also the `dotnet new` template ([ADR-0020](wiki/Decisions/0020-dotnet-new-templates.md),
`wiki/Getting-Started/Changing-The-Template.md`):

```bash
dotnet new install .                                           # both templates from this clone (uninstall the published pack first)
dotnet new starterkit -n Probe -o ../probe --sample false      # generate a product to check a template change
dotnet pack templates/StarterKit.Templates.csproj -o artifacts # the StarterKit.Templates pack (CI: templates.yml)
bash scripts/regenerate-module-migrations.sh                   # after changing the module template's model
cd docs-site && npm ci && npm run dev   # the documentation site (wiki/ rendered by Starlight)
cd docs-site && npm run check:mermaid   # every Mermaid diagram in wiki/ renders
```
<!--#endif-->

The SDK is pinned in `global.json` (10.0.x). All NuGet versions are in `Directory.Packages.props` (never put
`Version=` on a PackageReference); global settings are in `Directory.Build.props`. The API serves `/swagger` and
`/scalar`. Ports are in `.env` (see `README.md`).

## Project map

| Path | What |
|---|---|
| `src/Api` | Host: `Program.cs`, appsettings, Dockerfile, host-level endpoints |
| `src/Modules/<M>/<M>`, `<M>.Contracts` | Module implementation + its public contracts |
| `src/Modules/<M>/<M>.Tests`, `<M>.IntegrationTests` | Unit tests; integration tests on Testcontainers Postgres (Agentic, Catalog, DataTransfer, Identity, Shipping, Subscriptions, Tenancy, Webhooks; Attachment's live in `Attachment.Tests/Integration`) |
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
<!--#if (templateSource)-->
| `docs-site/` | Astro Starlight documentation site rendered from `wiki/`; navigation in `sidebar.mjs` |
| `apps/docs-v2/` | Independent Next.js/Fumadocs renderer of the same wiki; preserve V1, run V2 checks separately; operations in ADR-0024 |
| `.template.config/`, `templates/` | The `dotnet new` product template (this repo) and the module template + the `StarterKit.Templates` pack |
<!--#endif-->

**Modules:**
- **Identity**: auth, users, roles, OTP, 2FA.
- **Tenancy**: the control plane: tenants, branches, memberships, tenant roles, invitations, audit.
- **Attachment**: files.
- **Subscriptions**: plans, subscriptions, ledger, invoices.
<!--#if (sample)-->
- **Catalog**: the sample; products, categories, tags, wishlist.
- **Shipping**: the agent sample; shipments, customs declarations, the shipment preparation agent (the reference for
  adding an agent and its tools to a module) and the conversational `shipping.assistant`.
<!--#endif-->
- **Notification**: every email/WhatsApp delivery and its log, the in-app inbox + SignalR hub, push devices + FCM.
- **Webhooks**: outbound customer webhooks (tenant endpoints, signed deliveries, retries, delivery log).
- **DataTransfer**: bulk CSV/Excel import/export jobs; owning modules plug in definitions (`catalog.products`).
- **Agentic**: AI agents as durable runs; owning modules plug in agents and typed tools (`Agentic.Contracts`).

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

<!--#if (sample)-->
**Slice layout** (copy `src/Modules/Catalog`, or generate with `dotnet new starterkit-module`):
<!--#else-->
**Slice layout** (generate with `dotnet new starterkit-module`):
<!--#endif-->

```
Domains/<Aggregate>/
├── Models/            entity + children, static factories, private setters, invariants, events
├── Features/<Feature>/<Feature>Handler.cs   validator (first) + handler in one file
│                      <Feature>EndPoint.cs  Carter ICarterModule
├── Events/  EventHandlers/  Exceptions/  Consumers/  Jobs/  Services/  Mappings/
```

Small lookups may group handlers in one `<X>Handlers.cs` and their routes in a Carter `<X>sModule`. EF configurations live in `Data/Configurations/`, migrations in `Data/Migrations/`.
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

**Registration** ([ADR-0019](wiki/Decisions/0019-module-manifests-and-discovery.md)):
- `Add<M>Module` / `Use<M>Module` in `<M>Module.cs`, plus a `<M>ModuleManifest : ModuleBase` next to it with
  `[assembly: ModuleManifest(typeof(<M>ModuleManifest))]`. The manifest declares its `Order` (`ModuleOrder`), its
  `EnumAssemblies` (normally its Contracts, if it has enums) and delegates `AddServices` / `Initialize` to those
  extensions. `Initialize` never adds middleware.
- Nothing else to wire. `Api.csproj` references every `src/Modules/<M>/<M>/<M>.csproj` by glob, and
  `builder.AddModules()` discovers the manifests and registers MassTransit consumers, the enum lookup and the modules.
  The architecture tests take the module list from the host. Never re-add module calls to `Program.cs`.

**Startup order** (`src/Api/Program.cs`):
1. Service registration:
   1. `AddDockerSecrets` → `AddSharedInfrastructure` (auth policies, CORS, localization, paging, ...)
   2. `AddMultiTenancy` → Hangfire → rate limiting → Seq → `AddModules()` (MassTransit → enum lookup → modules)
   3. Modules in `ModuleOrder`: Identity 0 → Tenancy 100 → Attachment 200 → Catalog 300 → Subscriptions 400 →
      Webhooks 500 → DataTransfer 600 → **new modules 700** → Agentic 800 → Notification 900 (guarded by
      `ModuleDiscoveryTests`)
2. Middleware:
   1. `UseTrustedForwardedHeaders` (first) → `UseConfigurableCors` → `UseErrorHandling` (outside auth, so 401/403
      get JSON)
   2. `UseIdentityPipeline(afterAuthentication: a => a.UseMultiTenancy())` = authentication → tenant resolution →
      authorization
   3. `UseRateLimiter` (after auth, so partitions are per user) → Hangfire dashboard
   4. `UseModules()`: each module's `Initialize` in the same order (Tenancy before tenant-data seeding)
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
  - `.RequireHumanVerification("action")` on anonymous endpoints that create accounts or send mail/SMS (register, login,
    forgot-password, OTP resend). `IHumanVerifier` port, Turnstile adapter, token in `X-Captcha-Token`; 400
    `captcha_required` / `captcha_invalid`; inert unless `BotProtection:Enabled`
    (`wiki/Identity/Bot-Protection.md`).
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
  - The root version covers the children: a behaviour method that changes ONLY child rows calls `BumpVersionOnSave()`;
    one that also changes a root column needs nothing ([ADR-0021](wiki/Decisions/0021-aggregate-version-covers-child-collections.md)).
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
  - Privileged roles need 2FA (`wiki/Identity/Permissions-2FA-and-Admin-Audit.md`). A user-verified passkey
    sign-in counts as MFA.
- **Passkeys, SSO, SCIM** (Identity; `wiki/Identity/Passkeys.md`, `Enterprise-SSO.md`, `SCIM.md`):
  - Passkeys use the built-in `IPasskeyHandler<ApplicationUser>` only through `IPasskeyService`; ceremony state stays
    server-side and single use. Never add a cookie-based passkey flow.
  - SSO connections and SCIM directories are control-plane rows scoped by `TenantId` explicitly. Email linking and JIT
    apply only to DNS-verified domains (`SsoConnection.CoversVerified`). Tokens never travel in a URL: callbacks hand
    out a one-time code for `POST /api/auth/sso/exchange`, to an allow-listed `returnUrl`.
  - Every NON-SSO sign-in path (password, passkey, social) and password reset calls `ISsoEnforcementPolicy`.
  - SSO availability (sign-in, discovery, enforcement) is ONE check, `ISsoEntitlement` (plan feature `sso`):
    enforcement must lapse with sign-in, never block passwords while SSO is unavailable.
  - Role ids an admin assigns to directory-driven memberships go through `ITenantRoleGuard` (Identity, checks via Tenancy's contract).
  - Exceptions and aggregate invariants without DI localize through `IdentityText.Get(key, args)` (same resx).
  - Outbound calls to customer-supplied URLs (IdPs, webhooks) go through `Shared.Core.Net.GuardedConnector`.
  - Memberships from directories only via Tenancy `ProvisionMemberCommand`; deprovisioning suspends and revokes
    sessions, never deletes.
- **Notifications.** Inject `INotificationSender`:
  `.For(userId)` / `.ToEmail(addr)` → `.WithEmail(EmailTemplate.X, model.ToDictionary())` → `.SendAsync(...)`.
  Security mail forces `NotificationChannel.Email`. There is no other email API.
  - In-app and push are ADDITIVE channels (never in a channel order): `.WithInApp(new InAppContent(...))`,
    `.WithPush(...)`. Each channel group is its own Hangfire job sharing one correlation id; keep the chain's
    idempotency check on chain rows only.
  - Real time = the push-only hub `/hubs/notifications` (revoked sessions' sockets are closed by `UserSessionsRevokedConsumer`, fanned out to all replicas over Redis pub/sub) (`IRealtimeNotifier`, best effort, never throws). Commands
    stay REST. A token in the URL (`?access_token=`) is accepted ONLY under `RealtimePaths.HubPrefix`, and every
    revocation check runs on the validated token (`TokenBlacklistJwtBearerEvents`).
  - Email templates are built from the design-system components; colors only as tokens (`{{ t.* }}`, defined in
    `EmailDesignTokens`), never hex in a `.liquid` file (`EmailDesignSystemTests`). Preheader via `| set_preheader`;
    never a secret in it. Guide: `wiki/Modules/Notification-Email-Design-System.md`.
  - Categories are `<group>.<event>` (`NotificationCategoryGroups`: billing, security, membership, catalog,
    data-transfer, webhooks, agents, system). Users mute per group and channel and set quiet hours; fixed channels (all of
    `security`, `billing` email) cannot be muted and `security` ignores quiet hours. No category means never muted. Email-only
    sends name the category with `.InCategory(...)`.
  - Push goes only through `IPushProvider` (FCM adapter; FirebaseAdmin stays in Notification). Devices are bound to
    the registering session (`sid`). Guide: `wiki/Modules/Notification-Realtime-and-Push.md`.
- **Localization.**
  - User-facing text and validation messages use `IStringLocalizer<<M>Resource>`, with every key in both the `en`
    and `ar` resx files.
  - Bilingual data is `LocalizedText` (`name_en`/`name_ar` columns).
  - Enums exposed to clients get `[LocalizedDescription]` and are served by the enum lookup endpoint.
- **Audit trail.** Entities that need "who changed what" implement `IAuditable`; their DbContext maps
  `AddAuditTrail()` / `AddTenantAuditTrail()` (RLS in the migration for tenant data). `AuditTrailInterceptor` writes
  `audit_entries` in the same `SaveChanges`. Mark secrets and noisy fields `[AuditIgnore]`. Guarded by
  `AuditTrailArchitectureTests`.
- **Malware scanning** (Attachment, [ADR-0015](wiki/Decisions/0015-malware-scanning-clamd-fail-closed.md)):
  - Uploads are scanned through `IMalwareScanner`. The built-in adapter is ClamAV
    (`Attachment:Validation:Scanner:Provider=ClamAv`, `docker-compose.clamav.yml`).
  - It fails CLOSED: no verdict gives 503 `malware_scanner_unavailable`, infected gives 400 `malware_detected`. A
    custom scanner must throw when it cannot scan, never return clean.
  - The `clamav` health check reports Degraded only.
  - Production should set `RequireVirusScanning=true`.
- **Bulk import/export** (DataTransfer, [ADR-0016](wiki/Decisions/0016-bulk-data-transfer-on-hangfire.md)):
  - Never parse CSV/Excel or loop over thousands of rows in a request. Implement `IImportDefinition` /
    `IExportDefinition` (`DataTransfer.Contracts`) in the owning module and register it with `AddImportDefinition` /
    `AddExportDefinition` (reference: `Tenancy/Domains/Members/DataTransfer`).
  - `ApplyBatchAsync` must upsert by a natural key (batches replay after a crash), return one result per row, report
    bad rows as row errors (never throw for them), save nothing in a dry run, and act as `ImportContext.RequestedBy`.
  - Jobs run on the Hangfire queue `data-transfer`; `Hangfire:Servers:<Name>:Enabled` splits workloads across
    replicas of the same image (`docker-compose.worker.yml`). Don't add a separate worker host.
  - Background work on behalf of a user must re-check that user, run in THEIR branch scope (Hangfire restores the
    tenant only) and inside `ActingUser.Begin(userId)` (`Shared.Core.Web`: the "current user" when there is no
    request). `TransferJobRunner` is the pattern.
  - Live progress goes through `IUserEventPublisher` (`Notification.Contracts.Realtime`), throttled; the job row stays
    the source of truth.
- **AI agents** (Agentic, [ADR-0025](wiki/Decisions/0025-agentic-module-and-model-seam.md),
  [ADR-0026](wiki/Decisions/0026-durable-agent-runs-on-hangfire.md),
  [ADR-0027](wiki/Decisions/0027-agent-approvals-triggers-and-policies.md); guide: `wiki/Modules/Agentic.md`):
  - A module adds an agent with `AddAgent(new AgentDefinition { ... })` and tools with `AddAgentTool<T>()`
    (`Agentic.Contracts`). Never reference the Agentic implementation, `Microsoft.Extensions.AI` or a provider SDK
    outside the Agentic module.
  - A tool is `[AgentTool]` + `IAgentTool<TInput, TOutput>` and calls `ISender`. It never takes a DbContext, a
    repository or a service provider. It declares `Risk` and exactly one of `Permission` / `AllowAnyMember`
    (mutations always need a permission).
  - Tool inputs hold only what a model may choose: never a tenant id, a user id or a permission. Those come from
    `AgentToolContext`. Model output and tool results are untrusted; mark tools that return outside content
    `ReturnsUntrustedContent = true`.
  - The model only requests. Never add automatic function invocation to a chat client; every call goes through
    `ToolPolicy` (allowlist → the initiator's CURRENT permission → autonomy vs risk → approval) and is saved as a
    step before and after it runs.
  - Execution is at-least-once: make a mutation tool idempotent for `AgentToolContext.IdempotencyKey`, or declare
    `Idempotent = false` (it is then never repeated; the run stops with `tool_outcome_unknown`).
  - Business rules stay in commands, never in agent instructions. Prompts and tool content are not logged or traced
    unless `Agentic:Telemetry:CaptureContent` is on. Provider keys come from configuration or Docker secrets.
  - Approval is consent to ONE payload (tool, tool version, arguments, bound by a hash) and is used once. It never
    replaces a check: the executor decides the call again after a decision, and consumes the approval in the same
    save that marks the step executing. Never execute an approved action from the decision handler.
  - Automatic runs go through `RequestAgentRunCommand` from the owning module's consumer or job, with a stable
    `TriggerKey` and a real `OnBehalfOfUserId`. Never add a system identity for agents.
  - `AgentPolicy` only tightens (the stricter of policy and definition wins). Start every run through
    `AgentRunStarter`, which applies policy, capacity and the `agent_runs` quota.
  - Runs execute on the Hangfire queue `agentic` (`Hangfire:Servers:Agentic`); nothing runs in the request. The
    scripted provider (`IAgentScript`) is for tests and demos and is refused in Production.
  - Knowledge ([ADR-0028](wiki/Decisions/0028-agent-knowledge-and-memory-on-pgvector.md)): only what a module indexes
    is searchable. Register a source with `AddKnowledgeSource` (a `RequiredPermission`, or explicitly
    `AllowAnyMember`; optional `IKnowledgeSourcePolicy` for per-document access) and send
    `IndexKnowledgeDocumentCommand` / `RemoveKnowledgeDocumentCommand` from the owning module. Never add an upload
    endpoint or index content a module did not choose. The vector and full-text columns are SQL-managed
    (`KnowledgeStore`); every raw statement names the tenant as well as relying on RLS.
  - Memory is written by a person or proposed by `memory.remember` (`RequiresApproval`), and recalled only through
    the read tool `memory.recall`. Never inject memories or retrieved text into agent instructions.
  - Conversations ([ADR-0029](wiki/Decisions/0029-agent-conversations.md)): an agent opts in with
    `Conversational = true`; every message is a run started through `AgentRunStarter` with its `ConversationId` and
    `ConversationTurn`. History is rebuilt per model turn from earlier finished turns (message and final answer only,
    never old tool results). Keep one active turn per conversation (`ux_agent_runs_active_turn`) and the sticky taint
    (a turn starts tainted when an earlier one was); never put history into agent instructions.
- **Plan reads vs billing.** Feature gates and quotas use `ISubscriberIdProvider.GetPayingSubscriberId()` (any member;
  the tenant pays). `GetCurrentSubscriberId()` is for managing the subscription and needs `tenant.billing.manage`.
- **Caching.** Use Redis via `IDistributedCache`. Build tenant data keys with `ITenantCacheKeys.For(key)`.
  Invalidate explicitly from domain event handlers.
  Cache failures must never fail the request.
- **Web.** `IHttpContextService` is in `Shared.Core.Web`; add the `using` explicitly. Avoid `[FromServices]`; Carter
  resolves parameters.
- **Multi-tenancy** (`Shared.Core.MultiTenancy`, `wiki/MultiTenancy/*`):
  - **Data rules.** Business data lives in `ITenantDataContext`s (today: Catalog, Attachment, Webhooks, DataTransfer,
    Agentic).
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
<!--#if (sample)-->
| Catalog `ProductPublishedIntegrationEvent` → any | Integration |
<!--#endif-->
| Subscriptions `SubscriptionActivated/ExpiredIntegrationEvent` → Tenancy `TenantBillingStatusConsumer` | Integration |
| Attachment `UserAvatarChangedIntegrationEvent` → Identity `UserAvatarChangedConsumer` | Integration |
| Identity `UserDeletedIntegrationEvent` (in `UserDirectory.Contracts`) → Attachment / Tenancy / Notification / Agentic erasure | Integration |
| Agentic `AgentRunCompleted/FailedIntegrationEvent` → the module that owns the agent | Integration |
| Attachment `AttachmentDeletedIntegrationEvent` → Agentic removes knowledge indexed from the file | Integration |
| Identity `UserSessionsRevokedIntegrationEvent` (in `UserDirectory.Contracts`) → Notification `UserSessionsRevokedConsumer` closes hub sockets | Integration |
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
- Sagas are durable and explicit ([ADR-0023](wiki/Decisions/0023-durable-sagas-on-module-dbcontexts.md)): state in the
  owning module's DbContext (`SagaStateConfiguration<T>` + a migration, RLS for tenant data), registered in the
  manifest's `ConfigureMessaging` with `bus.AddDurableSagaStateMachine<TMachine, TState, TDbContext>()`. Never
  `AddSagaStateMachine`/`AddSaga` or an in-memory saga repository; the bus does not scan for sagas.
- Delivery is at-least-once (`MessageId` = `EventId`). Failed rows are dead-lettered after
  `Messaging:Outbox:MaxAttempts`; inspect/requeue them via `/api/v1/admin/outbox/{context}/dead-letters`
  (`outbox.manage`, SuperAdmin). Gauges `outbox.messages.pending` / `.dead_lettered` feed the Prometheus alerts.
- `Messaging:Transport` is `InMemory` (dev/tests, NOT durable), `PostgreSql` (the production default) or `RabbitMq`.
  Production must not run `InMemory`; `ProductionConfigurationGuard` refuses to boot with it, and with `RabbitMq` that
  has no broker address. `--migrate-only` owns the transport schema ([ADR-0022](wiki/Decisions/0022-production-message-transport.md)).
- Bus behavior is pinned by `Shared.Core.Tests/Messaging/TenantMessagingBusTests`, which runs on InMemory AND
  PostgreSql: tenant propagation, purged-tenant drop, migrating-tenant redelivery, no noisy-neighbor starvation. Never
  remove `WaitUntilStarted` (lost messages at startup) or swap the migrating-tenant delayed redelivery back to an
  in-memory retry (starves other tenants). Messages that fail every retry go to `<endpoint>_error`; the
  `MessageConsumerFaults` alert reports them.

## Architecture guardrails (`src/Api.Tests/Architecture/`; run `dotnet test src/Api.Tests`)

| Test | Fails when |
|---|---|
| `ModuleDiscoveryTests` | A `src/Modules` folder is not discovered by the host, a module lacks exactly one `<M>ModuleManifest`, or `ModuleOrder` breaks a startup constraint |
| `ModuleBoundaryTests` | A module references another module's implementation; Contracts reference implementations; Shared references a module; a non-Identity module uses `Identity.Contracts`. The module list is discovered from the host. |
| `NamingConventionTests` | Handler/Command/Query/Validator/Event/EndPoint/Module suffixes are wrong, or handlers sit in Contracts |
| `ThirdPartyIsolationTests` | MediatR or ImageSharp is referenced; SkiaSharp leaves Attachment (image adapter) or QuestPDF leaves Subscriptions (licenses: `wiki/Reference/Third-Party-Licenses.md`); FirebaseAdmin/Google.Apis or the SignalR Redis backplane leave Notification; Sylvan (CSV/XLSX) leaves DataTransfer; Microsoft.Extensions.AI, a model-provider SDK (OpenAI, Anthropic) or PdfPig leaves Agentic; a Contracts project references the bus (only `MassTransit.Abstractions` for `[MessageUrn]`) |
| `MessagingArchitectureTests` | Events are outside `.Contracts/.../IntegrationEvents`, have a bad or duplicate URN, or are also mediator notifications; consumers sit outside modules; outbox contexts and registrations mismatch; Contracts reference Shared.Core/Messaging. **Register new events in the expected list.** |
| `SagaArchitectureTests` | A saga is registered through MassTransit directly (in-memory repository) instead of `AddDurableSagaStateMachine`, or its state is not mapped in a module DbContext |
| `BackgroundJobEventDispatchTests` | A job, orchestrator, hosted service or consumer saves without `IUnitOfWorkRunner`/`IDomainEventDispatcher` |
| `TenantIsolationArchitectureTests` | A tenant entity lacks `IMustHaveTenant`; a query filter is unnamed; a unique index lacks `tenant_id` or does not start with it; parameterless `IgnoreQueryFilters()` appears |
| `EnumStorageTests` | An enum column of any module context is not stored as a string (nine legacy integer columns are listed; never add to the list) |
| `AdminEndpointAuthorizationTests` | An `/admin` or `/superadmin` route has no admin or permission policy; a used permission policy is unregistered; **any endpoint declares no authorization at all** |
| `AgentToolArchitectureTests` | An agent tool takes a DbContext, a repository, `IServiceProvider` or `IServiceScopeFactory` instead of calling `ISender`, or a tool class lacks `[AgentTool]` / `IAgentTool<,>` |
| `ClientGeneratedKeyTests` | A child entity (reached through a collection navigation) has a store-generated Guid key: needs `ValueGeneratedNever()` |
| `PendingModelChangesTests` (`Api.E2ETests`) | The EF model changed without a migration |
| `TenantRowLevelSecurityCoverageTests` (`Api.E2ETests`) | A table with `tenant_id` in a tenant-data schema lacks forced RLS + the `tenant_isolation` policy |

## Testing

- **Stack:** xUnit v3 + FluentAssertions 7 + Moq 4, plus Testcontainers (`postgres:16-alpine`) for integration and E2E
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

<!--#if (aiTooling)-->
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
| AI agent / agent tool in a module | `/starterkit:add-agent-tool` |
| Scheduled / background job | `/starterkit:add-background-job` |
| Permission / endpoint authorization | `/starterkit:add-permission` |
| Email / WhatsApp message | `/starterkit:add-email-template` |
| Tests at any level | `/starterkit:write-tests` |
| Code review (interactive, sectioned) | `/starterkit:review` (the `starterkit:architecture-reviewer` agent does the guardrail pass) |
| New product from the template | `/starterkit:new-project` |
| Documentation | `/starterkit:update-docs` |

The plugin's hooks keep git work off `master`/`develop` (no force-push, no `--no-verify`, no PR merge) and warn on
forbidden C# patterns after edits. Spec Kit
(`/speckit-*`) is available for spec → plan → tasks flows; its principles are in `.specify/memory/constitution.md`.
<!--#endif-->

## Documentation

- Diagram sources follow `wiki/Reference/Diagram-Style.md`: verify relationships against code, declare the view,
  use accessible titles/descriptions and semantic roles, and validate both themes before review.
- The index of all docs is `wiki/INDEX.md`. Read the relevant page before changing an area: Architecture,
  MultiTenancy, Messaging, Modules, Identity, Attachment, Testing, Deployment, Decisions (ADRs).
<!--#if (templateSource)-->
- `wiki/` is rendered to the documentation site; every new page also needs an entry in `docs-site/sidebar.mjs`.
<!--#endif-->
- **Routing:** reference material every developer needs goes in `wiki/`. Flow walkthroughs, session analyses and
  investigations go in your notes vault (`<vault>/projects/<project>/`). Never add markdown to the repo root (only
  `README.md`, `CLAUDE.md`, `AGENTS.md`).
- When behaviour or structure changes, update the wiki page, `wiki/INDEX.md`, `README.md` and this file in the same
  change. Architectural decisions get an ADR in `wiki/Decisions/`. Check links with `bash scripts/check-docs.sh`.

## Rules

- Documentation-only changes, including Markdown/MDX, diagrams, documentation CI and the Astro/Fumadocs/Next.js
  documentation applications, do not require a .NET solution/API build or .NET tests. Run the applicable documentation
  checks, site build and browser tests instead. Do not start the API or export OpenAPI unless backend contracts changed
  or the user explicitly requests contract verification. Validate template packaging separately when its rules change.
- After backend/.NET code changes, run `dotnet build StarterKit.slnx` from the repo root and fix every error, then run
  the relevant tests and `dotnet test src/Api.Tests`.
- The build treats warnings as errors: fix them, or relax a rule in `.editorconfig` with a justification; never `#pragma`
  without one (`wiki/Architecture/CodingStandards.md` → Analyzers).
- **Git: commit and open PRs only when the user asks, and only on a feature branch.**
  - Branch first (`feature/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`). Never commit on, push to, or
    merge into `master` / `develop`; they change only through a reviewed PR.
  - Before committing backend/.NET code changes, the solution build, relevant tests and `dotnet test src/Api.Tests`
    must pass. Documentation-only commits, including documentation application code, require the applicable documentation
    checks and site build instead. Stage only the files of the
    change (no secrets, build output or scratch files). Use a conventional commit message
    (`feat(module): ...`, `fix: ...`).
  - Open the PR with `gh pr create --base master` (or the base the user names). The body has a summary and the test
    plan with real results.
  - Never force-push, rewrite pushed history, skip hooks (`--no-verify`), merge a PR, or delete a remote branch: the
    user reviews and merges.
  - The plugin hook `guard-git.sh` enforces these limits.
- Never put real credentials, hostnames or IPs in committed files. Use `CHANGE_ME`, `api.example.com`,
  `YOUR_SERVER_IP`. Secrets come from `./secrets/*.txt` (Docker secrets, git-ignored) or environment variables.
- CI/CD:
  - `.github/workflows/hybrid-ci-cd.yml` runs build and test on pushes to `master`/`develop` and on PRs.
  - The GHCR image and the deploy run only from **Run workflow** (manual `workflow_dispatch`): build, wait for
    approval (the `production` environment), then deploy on the self-hosted runner. A merge to `master` does not
    deploy.
  - `rollback.yml` runs a previous API image (`image_tag`) and/or restores the pre-deploy database dump, then
    migrates and restarts the API; `deployment/rollback.sh <tag>` does the image part on the server.
- Docker Compose publishes host ports on `BIND_ADDRESS` (default `127.0.0.1`): Docker bypasses the host firewall, so
  never publish the database or Redis on all interfaces. PostgreSQL runs the image built from `infra/postgres`
  (pgBackRest and pgvector inside); keep `init: true` on it. PostgreSQL test containers that run the Agentic
  migrations use `pgvector/pgvector:0.8.7-pg16`.
<!--#if (templateSource)-->
- **This repo is the template** (ADR-0020, `wiki/Getting-Started/Changing-The-Template.md`). Optional code is
  conditional, never deleted (the page lists the marker syntax per file type; docs that ship in products never quote
  it). Never put an MSBuild `Condition` attribute on an element that must survive generation (`dotnet new` drops it).
  Tests use vehicles every product has (not Catalog) unless they test Catalog. `templates.yml` must stay green.
<!--#endif-->
- Starting a new product from this template: `dotnet new starterkit` (`wiki/Getting-Started/New-Project.md`) or
  `/starterkit:new-project`.
