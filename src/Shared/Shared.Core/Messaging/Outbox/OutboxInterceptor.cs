using System.Text.Json;
using Shared.Contracts.Messaging;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// Transactional outbox, write side. In <c>SavingChanges</c> it takes the integration events off every tracked
/// aggregate and adds one outbox row per event to the SAME context - so the rows are inserted by the same SaveChanges
/// (same database transaction) as the aggregate: they exist if and only if the change was committed.
/// <para>
/// It also stages an <see cref="OutboxMessagesSaved"/> notification in the scope's <see cref="IDomainEventCollector"/>:
/// the collector's commit tracking (failed save / rollback ⇒ discarded) guarantees immediate delivery is only attempted
/// for committed rows, after the outermost unit of work. Anything not delivered then is picked up by
/// <see cref="OutboxSweepJob"/>.
/// </para>
/// Registered by <c>AddCustomDbContext</c> AFTER <see cref="TenantEntityInterceptor"/>, so aggregate tenant ids are
/// already stamped and validated when they are copied onto tenant outbox rows.
/// </summary>
public sealed class OutboxInterceptor(
    IDomainEventCollector collector,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Write(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Write(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Write(DbContext? context)
    {
        if (context is null)
            return;

        var aggregates = context.ChangeTracker.Entries<IAggregate>()
            .Where(e => e.Entity.IntegrationEvents.Count > 0)
            .ToList();
        if (aggregates.Count == 0)
            return;

        var messageType = context.FindOutboxMessageType()
                          ?? throw new InvalidOperationException(
                              $"{context.GetType().Name} saves aggregates that raised integration events " +
                              $"({string.Join(", ", aggregates.SelectMany(a => a.Entity.IntegrationEvents).Select(e => e.GetType().Name).Distinct())}) " +
                              "but maps no outbox. Call builder.AddOutbox() (host context) or builder.AddTenantOutbox() " +
                              "(tenant-data context) in OnModelCreating, add a migration and register services.AddOutbox<TContext>().");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var traceParent = Activity.Current?.Id;
        var ids = new List<Guid>();

        foreach (var entry in aggregates)
        {
            // Validate every event BEFORE taking any off the aggregate: a missing urn must not lose events.
            var events = entry.Entity.IntegrationEvents.ToList();
            var names = events.Select(e => IntegrationEventTypes.NameOf(e.GetType())).ToList();
            entry.Entity.ClearIntegrationEvents();

            for (var i = 0; i < events.Count; i++)
            {
                var row = Create(messageType, entry, events[i], names[i], now, traceParent);
                context.Add((object)row); // non-generic: mapped by the runtime row type
                ids.Add(row.Id);
            }
        }

        collector.Stage(context, [new OutboxMessagesSaved(context.GetType(), ids, tenantContext.Tenant,
            tenantContext.IsHostScope)]);
    }

    private OutboxMessageBase Create(Type messageType, EntityEntry<IAggregate> entry, IIntegrationEvent @event,
        string name, DateTime now, string? traceParent)
    {
        OutboxMessageBase row;
        if (messageType == typeof(TenantOutboxMessage))
        {
            // The aggregate's (already stamped and validated) tenant - correct in host scope too.
            var tenantId = entry.Entity is IMustHaveTenant { TenantId: var owned } && owned != Guid.Empty
                ? owned
                : tenantContext.TenantId ?? throw new TenantNotResolvedException(
                    $"cannot write the integration event {@event.GetType().Name} without a tenant.");
            row = new TenantOutboxMessage { TenantId = tenantId };
        }
        else
        {
            row = new OutboxMessage { TenantId = tenantContext.TenantId };
        }

        row.Id = @event.EventId == Guid.Empty ? Guid.NewGuid() : @event.EventId;
        row.MessageType = name;
        row.Payload = JsonSerializer.Serialize(@event, @event.GetType(), Json);
        row.OccurredOn = @event.OccurredOn == default ? now : @event.OccurredOn;
        row.HostScope = tenantContext.IsHostScope;
        row.TraceParent = traceParent;
        row.NextAttemptAt = now;
        return row;
    }
}

/// <summary>
/// Internal notification: outbox rows were saved by <paramref name="ContextType"/>. Staged in the domain-event collector,
/// so it is only published once those rows are COMMITTED (and after the outermost unit of work); handled by
/// <see cref="OutboxMessagesSavedHandler"/>. <paramref name="Tenant"/>/<paramref name="IsHostScope"/> are the ambient
/// scope of the save - the delivery re-enters it to open the same database with the same isolation.
/// </summary>
public sealed record OutboxMessagesSaved(
    Type ContextType,
    IReadOnlyList<Guid> MessageIds,
    TenantInfo? Tenant,
    bool IsHostScope) : INotification;
