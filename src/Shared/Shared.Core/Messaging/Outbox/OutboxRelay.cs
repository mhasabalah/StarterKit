using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// Transactional outbox, read side: claims pending rows of ONE context, publishes them to the bus and records the
/// outcome. Used by <see cref="OutboxMessagesSavedHandler"/> (immediate, right after the commit) and
/// <see cref="OutboxSweepJob"/> (safety net).
/// <para><b>Guarantees.</b></para>
/// <list type="bullet">
/// <item><b>At-least-once.</b> A row is marked processed only after the publish succeeded; a crash in between
/// re-publishes it (same MessageId = EventId). Consumers must be idempotent.</item>
/// <item><b>No double claim.</b> Rows are leased with a conditional UPDATE whose predicate is on the target row,
/// so concurrent relays (other instances, immediate delivery vs. sweep) never own the same row at once.</item>
/// <item><b>Isolation preserved.</b> Only EF queries (named tenant filter + RLS stay active); the caller supplies a
/// context created in the right tenant scope. Each message is published inside ITS tenant scope, so the tenant
/// publish filter stamps the correct header and consumers run as that tenant.</item>
/// <item><b>Bounded retries.</b> Exponential back-off, dead-lettered after <see cref="OutboxOptions.MaxAttempts"/>.</item>
/// </list>
/// Ordering is best-effort (by <c>OccurredOn</c>); a retried message can overtake a later one. Consumers reconcile
/// from the source of truth instead of trusting arrival order.
/// </summary>
public sealed class OutboxRelay(
    IPublishEndpoint publishEndpoint,
    ITenantStore tenantStore,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxRelay> logger)
{
    public const string ActivitySourceName = "StarterKit.Outbox";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Delivers pending rows of <paramref name="context"/> (only <paramref name="messageIds"/> when given) in rounds of
    /// <see cref="OutboxOptions.BatchSize"/>. Returns the number of rows published.
    /// </summary>
    public Task<int> DeliverAsync(DbContext context, IReadOnlyCollection<Guid>? messageIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messageType = context.FindOutboxMessageType()
                          ?? throw new InvalidOperationException($"{context.GetType().Name} maps no outbox.");

        return messageType == typeof(TenantOutboxMessage)
            ? DeliverAsync<TenantOutboxMessage>(context, messageIds, cancellationToken)
            : DeliverAsync<OutboxMessage>(context, messageIds, cancellationToken);
    }

    /// <summary>Deletes delivered rows older than <see cref="OutboxOptions.RetentionDays"/>. Dead letters are kept.</summary>
    public Task<int> PurgeDeliveredAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        return context.FindOutboxMessageType() == typeof(TenantOutboxMessage)
            ? context.Set<TenantOutboxMessage>().Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken)
            : context.Set<OutboxMessage>().Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Backlog of <paramref name="context"/> as the filters/scope of the caller see it (feeds <see cref="OutboxMetrics"/>).</summary>
    public async Task<(long Pending, long DeadLettered)> CountBacklogAsync(DbContext context,
        CancellationToken cancellationToken = default)
    {
        return context.FindOutboxMessageType() == typeof(TenantOutboxMessage)
            ? await CountAsync<TenantOutboxMessage>(context, cancellationToken)
            : await CountAsync<OutboxMessage>(context, cancellationToken);

        static async Task<(long, long)> CountAsync<TMessage>(DbContext db, CancellationToken ct)
            where TMessage : OutboxMessageBase
        {
            var counts = await db.Set<TMessage>().AsNoTracking()
                .Where(m => m.ProcessedAt == null)
                .GroupBy(m => m.DeadLetteredAt != null)
                .Select(g => new { DeadLettered = g.Key, Count = g.LongCount() })
                .ToListAsync(ct);
            return (counts.Where(c => !c.DeadLettered).Sum(c => c.Count),
                counts.Where(c => c.DeadLettered).Sum(c => c.Count));
        }
    }

    private async Task<int> DeliverAsync<TMessage>(DbContext context, IReadOnlyCollection<Guid>? messageIds,
        CancellationToken cancellationToken) where TMessage : OutboxMessageBase
    {
        var settings = options.Value;
        var published = 0;

        while (true)
        {
            var claimed = await ClaimAsync<TMessage>(context, messageIds, settings, cancellationToken);
            if (claimed.Count == 0) return published;

            var delivered = new List<Guid>(claimed.Count);
            foreach (var row in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await PublishAsync(row, cancellationToken);
                if (outcome is null)
                {
                    delivered.Add(row.Id);
                }
                else
                {
                    await RecordFailureAsync<TMessage>(context, row, outcome.Value, settings, cancellationToken);
                }
            }

            if (delivered.Count > 0)
            {
                var token = claimed[0].LockToken;
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await context.Set<TMessage>()
                    .Where(m => delivered.Contains(m.Id) && m.LockToken == token)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.ProcessedAt, now)
                        .SetProperty(m => m.LockedUntil, (DateTime?)null)
                        .SetProperty(m => m.LockToken, (Guid?)null)
                        .SetProperty(m => m.LastError, (string?)null), cancellationToken);
                published += delivered.Count;
            }

            // Targeted delivery handles its ids in one round; a partial batch means the backlog is drained.
            if (messageIds is not null || claimed.Count < settings.BatchSize) return published;
        }
    }

    private async Task<List<TMessage>> ClaimAsync<TMessage>(DbContext context, IReadOnlyCollection<Guid>? messageIds,
        OutboxOptions settings, CancellationToken cancellationToken) where TMessage : OutboxMessageBase
    {
        var set = context.Set<TMessage>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var pending = set.AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= now &&
                        (m.LockedUntil == null || m.LockedUntil < now));
        if (messageIds is not null)
        {
            if (messageIds.Count == 0) return [];
            pending = pending.Where(m => messageIds.Contains(m.Id));
        }

        var candidates = await pending
            .OrderBy(m => m.OccurredOn)
            .Select(m => m.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        // The lease predicate is repeated on the target rows: under concurrency Postgres re-checks it after waiting
        // for the competing UPDATE, so a row leased by someone else in the meantime is skipped, never stolen.
        var token = Guid.NewGuid();
        var leaseUntil = now.AddSeconds(settings.LeaseSeconds);
        var count = await set
            .Where(m => candidates.Contains(m.Id) && m.ProcessedAt == null && m.DeadLetteredAt == null &&
                        (m.LockedUntil == null || m.LockedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.LockToken, token)
                .SetProperty(m => m.LockedUntil, leaseUntil), cancellationToken);
        if (count == 0) return [];

        return await set.AsNoTracking()
            .Where(m => m.LockToken == token)
            .OrderBy(m => m.OccurredOn)
            .ToListAsync(cancellationToken);
    }

    /// <returns><c>null</c> when published; otherwise the failure (and whether it is permanent).</returns>
    private async Task<(string Error, bool Permanent)?> PublishAsync(OutboxMessageBase row,
        CancellationToken cancellationToken)
    {
        var type = IntegrationEventTypes.Resolve(row.MessageType);
        if (type is null)
        {
            return ($"Unknown integration event type '{row.MessageType}' (renamed urn or removed event?).", true);
        }

        object message;
        try
        {
            message = JsonSerializer.Deserialize(row.Payload, type, OutboxInterceptor.Json)
                      ?? throw new JsonException("payload deserialized to null");
        }
        catch (JsonException ex)
        {
            return ($"Payload of '{row.MessageType}' cannot be read: {ex.Message}", true);
        }

        var tenantId = row switch
        {
            TenantOutboxMessage t => t.TenantId,
            OutboxMessage h => h.TenantId,
            _ => null
        };

        TenantInfo? tenant = null;
        if (tenantId is { } id)
        {
            tenant = await tenantStore.FindByIdAsync(id, cancellationToken);
            if (tenant is null)
            {
                return ($"Tenant {id} no longer exists.", true);
            }
        }

        try
        {
            // Publish as the flow that raised the event: the tenant publish filter stamps the header from it.
            // No database work inside this scope (the caller's context is bound to the caller's scope).
            using (row.HostScope ? TenantScope.BeginHostScope(tenant) : tenant is null ? TenantScope.BeginEmpty() : TenantScope.Begin(tenant))
            using (var activity = ActivitySource.StartActivity("outbox publish", ActivityKind.Producer, row.TraceParent))
            {
                activity?.SetTag("messaging.message.id", row.Id);
                activity?.SetTag("messaging.message.type", row.MessageType);

                await publishEndpoint.Publish(message, type, context => context.MessageId = row.Id, cancellationToken);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ($"{ex.GetType().Name}: {ex.Message}", false);
        }
    }

    private async Task RecordFailureAsync<TMessage>(DbContext context, TMessage row, (string Error, bool Permanent) failure,
        OutboxOptions settings, CancellationToken cancellationToken) where TMessage : OutboxMessageBase
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attempts = row.Attempts + 1;
        var deadLetter = failure.Permanent || attempts >= settings.MaxAttempts;
        var error = failure.Error.Length > OutboxMessageBase.LastErrorMaxLength
            ? failure.Error[..OutboxMessageBase.LastErrorMaxLength]
            : failure.Error;
        var nextAttemptAt = now.Add(OutboxOptions.BackoffFor(attempts));
        DateTime? deadLetteredAt = deadLetter ? now : null;

        await context.Set<TMessage>()
            .Where(m => m.Id == row.Id && m.LockToken == row.LockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                .SetProperty(m => m.DeadLetteredAt, deadLetteredAt)
                .SetProperty(m => m.LockedUntil, (DateTime?)null)
                .SetProperty(m => m.LockToken, (Guid?)null), cancellationToken);

        if (deadLetter)
        {
            logger.LogError("Outbox message {MessageId} ({MessageType}) DEAD-LETTERED after {Attempts} attempt(s): {Error}",
                row.Id, row.MessageType, attempts, error);
        }
        else
        {
            logger.LogWarning("Outbox message {MessageId} ({MessageType}) publish failed (attempt {Attempts}), retry at {NextAttemptAt:O}: {Error}",
                row.Id, row.MessageType, attempts, nextAttemptAt, error);
        }
    }
}
