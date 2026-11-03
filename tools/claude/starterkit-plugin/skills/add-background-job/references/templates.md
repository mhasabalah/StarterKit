# add-background-job template

```csharp
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;
using Shared.Core.MultiTenancy.Data;        // IgnoreNonIsolationFilters()
using {{Module}}.Options;

namespace {{Module}}.Domains.{{Aggregate}}s.Jobs;

/// <summary>
/// Recurring (scheduled in Use{{Module}}Module). Runs once per tenant; each item is processed in its own unit of work,
/// so its domain events are published after its commit and one failure never affects the next item.
/// </summary>
[DisableConcurrentExecution(60)]
public class Expire{{Aggregate}}sJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<{{Module}}Options> options,
    ILogger<Expire{{Aggregate}}sJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(
            async (_, _) => total += await RunForCurrentTenantAsync(cancellationToken),
            nameof(Expire{{Aggregate}}sJob));
        return total;
    }

    public async Task<int> RunForCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        cancellationToken?.ThrowIfCancellationRequested();
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var ids = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<{{Module}}DbContext>().{{Aggregate}}s
                .AsNoTracking()
                .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
                .OrderBy(x => x.ExpiresAt)
                .Select(x => x.Id)
                .Take(settings.ExpireBatchSize)
                .ToListAsync(ct), CancellationToken.None);

        var done = 0;
        foreach (var id in ids)
        {
            cancellationToken?.ThrowIfCancellationRequested();
            try
            {
                await unitOfWork.RunAsync(
                    (services, ct) => services.GetRequiredService<ISender>().Send(new Expire{{Aggregate}}Command(id), ct),
                    CancellationToken.None);
                done++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                logger.LogWarning(ex, "Expiring {{aggregate}} {Id} skipped: modified concurrently", id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expiring {{aggregate}} {Id} failed", id);
            }
        }

        logger.LogInformation("Expire{{Aggregate}}sJob: {Done}/{Found} expired", done, ids.Count);
        return done;
    }
}
```

## Registration and schedule (`{{Module}}Module.cs`)

```csharp
public const string Expire{{Aggregate}}sJobId = "{{module}}-expire-{{aggregate}}s";

// Add{{Module}}Module
services.AddTransient<Expire{{Aggregate}}sJob>();

// Use{{Module}}Module
using var scope = app.ApplicationServices.CreateScope();
var settings = scope.ServiceProvider.GetRequiredService<IOptions<{{Module}}Options>>().Value;
scope.ServiceProvider.GetRequiredService<IRecurringJobManager>()
    .AddOrUpdate<Expire{{Aggregate}}sJob>(Expire{{Aggregate}}sJobId,
        x => x.ExecuteAsync(JobCancellationToken.Null), settings.ExpireCron);
```

## appsettings

```json
"{{Module}}": {
  "ExpireCron": "*/15 * * * *",
  "ExpireBatchSize": 100
}
```
