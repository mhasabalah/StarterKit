---
name: add-background-job
description: Add a Hangfire recurring or fire-and-forget background job to a StarterKit module that is tenant-aware (ITenantJobRunner), does DB work through IUnitOfWorkRunner so domain events are published, uses TimeProvider and options-bound cron, and skips concurrency conflicts. Use for "scheduled job", "cron", "nightly cleanup", "recurring task", "background processing", "Hangfire".
---

# Add a background job (Hangfire)

**Main rule:** jobs, hosted services and orchestrators run **outside MediatR**, so nothing publishes their domain
events unless you do. All DB work goes through `IUnitOfWorkRunner.RunAsync`, one call per item. Don't hand-roll
`IServiceScopeFactory` scopes; `BackgroundJobEventDispatchTests` enforces this by IL scan.

## Inputs

- Module, job purpose, schedule (cron, UTC) or trigger (enqueued from a handler or a consumer).
- Per tenant, or platform-wide?
- Batch size and retention settings (options).

## Steps

Template: [references/templates.md](references/templates.md). The reference job is
`src/Modules/Catalog/Catalog/Domains/Products/Jobs/PurgeSoftDeletedProductsJob.cs`.

1. **Options.** Add `CronExpression`, `BatchSize` and so on to `<M>Options` (`Options/<M>Options.cs`) with safe
   defaults, plus the matching section in `src/Api/appsettings.json`.
2. **Job class** `Domains/<X>s/Jobs/<Verb><X>sJob.cs`:
   - `[DisableConcurrentExecution(60)]` so runs never overlap.
   - Inject `IUnitOfWorkRunner`, `ITenantJobRunner` (when it touches tenant data), `TimeProvider`,
     `IOptions<<M>Options>` and `ILogger<T>`.
   - `ExecuteAsync(IJobCancellationToken? cancellationToken)` calls
     `tenantJobs.ForEachTenantScopeAsync((_, _) => ..., nameof(Job))`, which gives each tenant its own scope,
     filters and RLS, so one tenant's failure never stops another.
   - Select ids in one `RunAsync` (`AsNoTracking`, `Take(batch)`), then process **each id in its own** `RunAsync`,
     preferably by sending a command (`ISender`) so validation, behaviors and events apply.
   - `catch (Exception ex) when (ex.IsConcurrencyConflict())` logs and skips. Any other exception is logged too,
     and the loop continues; one item must never abort the batch.
   - Take "now" from `timeProvider.GetUtcNow()`, never `DateTime.UtcNow`.
   - Return a count, so it's useful in tests and logs.
3. **Register** in `<M>Module.cs`: `services.AddTransient<<Verb><X>sJob>();`.
4. **Schedule** in `Use<M>Module` with
   `recurringJobManager.AddOrUpdate<Job>(<M>Module.<Job>Id, x => x.ExecuteAsync(JobCancellationToken.Null), options.Cron)`.
   Give the job a stable `const string` id; renaming it leaves an orphan recurring job.
   For fire-and-forget work, call `IBackgroundJobClient.Enqueue<Job>(x => ...)` from a handler or consumer. The
   tenant flows automatically.
5. **Tests**
   - Unit-test the decision logic.
   - Integration-test the job against Testcontainers Postgres: seed expired and non-expired rows, run
     `PurgeCurrentTenantAsync`-style methods directly, and assert only the right rows changed and that events fired.
     See `Catalog.IntegrationTests/ProductLifecycleTests`.

## Guardrails

- `BackgroundJobEventDispatchTests`: a class named `*Job`, `*Orchestrator`, an `IHostedService` or a consumer that
  saves must depend on `IUnitOfWorkRunner` or `IDomainEventDispatcher`.
- `TenantIsolationArchitectureTests`: no parameterless `IgnoreQueryFilters()`. Use `IgnoreNonIsolationFilters()`.
- A `Migrating` tenant is read-only, and the tenant runner defers it automatically. Don't bypass that.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Api.Tests --filter "FullyQualifiedName~BackgroundJob"
dotnet test src/Modules/<M>/<M>.IntegrationTests --filter "FullyQualifiedName~<Job>"
```

Then run the API, open `/hangfire` (admin), confirm the recurring job is listed, and trigger it once.
