using Hangfire;
using Microsoft.Extensions.Options;
using Shared.Core.Messaging.Outbox;

namespace Api.Infrastructure;

/// <summary>
/// Schedules the transactional-outbox safety net. Immediate delivery happens right after each commit
/// (<see cref="OutboxMessagesSavedHandler"/>); <see cref="OutboxSweepJob"/> re-delivers whatever that missed
/// (crash, broker outage, saves outside a unit of work) and purges delivered rows. Hangfire runs a recurring job on one
/// server at a time, and the relay's row leases make overlapping runs safe anyway.
/// </summary>
public static class OutboxConfiguration
{
    public static IApplicationBuilder UseOutboxSweep(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<OutboxOptions>>().Value;

        app.ApplicationServices.GetRequiredService<IRecurringJobManager>().AddOrUpdate<OutboxSweepJob>(
            OutboxSweepJob.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            options.SweepCron);

        return app;
    }
}
