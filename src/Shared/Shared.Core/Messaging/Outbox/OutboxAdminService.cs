using Shared.Contracts.Pagination;
using Shared.Core.Data;
using Shared.Core.Exceptions;
using Shared.Core.Extensions;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Messaging.Outbox;

/// <summary>A dead-lettered outbox row as shown to operators (the payload is deliberately not exposed).</summary>
public sealed record OutboxDeadLetterDto(
    Guid Id,
    string MessageType,
    Guid? TenantId,
    DateTime OccurredOn,
    DateTime? DeadLetteredAt,
    int Attempts,
    string? LastError);

/// <summary>
/// Operator access to dead-lettered outbox rows (host-level endpoints, platform permission). Replaces hand-written SQL.
/// <list type="bullet">
/// <item><b>Context</b> = the DbContext type name registered with <c>AddOutbox&lt;T&gt;()</c> (case-insensitive).</item>
/// <item>Host contexts: their own table. Tenant-data contexts: without <c>tenantId</c> the call runs in a small explicit
/// host scope (all tenants of the SHARED database); with <c>tenantId</c> it runs as that tenant, so a tenant on a
/// DEDICATED database is reachable too.</item>
/// <item><b>Requeue</b> resets attempts and clears the dead-letter mark; the next outbox sweep (within a minute by
/// default) delivers the row. It never touches rows that are not dead-lettered.</item>
/// </list>
/// </summary>
public sealed class OutboxAdminService(
    OutboxRegistry registry,
    IUnitOfWorkRunner runner,
    ITenantStore tenantStore,
    TimeProvider timeProvider,
    ILogger<OutboxAdminService> logger)
{
    public Task<PaginatedResult<OutboxDeadLetterDto>> ListDeadLettersAsync(string context, Guid? tenantId, int? page,
        int? pageSize, CancellationToken cancellationToken) =>
        InScopeAsync(context, tenantId, (db, type, ct) => type == typeof(TenantOutboxMessage)
                ? ListAsync<TenantOutboxMessage>(db, page, pageSize, ct)
                : ListAsync<OutboxMessage>(db, page, pageSize, ct),
            cancellationToken);

    /// <exception cref="NotFoundException">Unknown context, or no such row.</exception>
    /// <exception cref="ConflictException">The row exists but is not dead-lettered.</exception>
    public async Task RequeueAsync(string context, Guid id, Guid? tenantId, CancellationToken cancellationToken)
    {
        var outcome = await InScopeAsync(context, tenantId, (db, type, ct) => type == typeof(TenantOutboxMessage)
                ? RequeueAsync<TenantOutboxMessage>(db, id, ct)
                : RequeueAsync<OutboxMessage>(db, id, ct),
            cancellationToken);

        if (outcome == RequeueOutcome.NotFound)
            throw new NotFoundException("Outbox message", id);
        if (outcome == RequeueOutcome.NotDeadLettered)
        {
            throw new ConflictException($"Outbox message {id} is not dead-lettered; nothing to requeue.");
        }

        logger.LogWarning("Outbox message {MessageId} of {Context} requeued by an operator", id, context);
    }

    /// <returns>Number of dead letters requeued.</returns>
    public async Task<int> RequeueAllAsync(string context, Guid? tenantId, CancellationToken cancellationToken)
    {
        var count = await InScopeAsync(context, tenantId, (db, type, ct) => type == typeof(TenantOutboxMessage)
                ? RequeueAllAsync<TenantOutboxMessage>(db, ct)
                : RequeueAllAsync<OutboxMessage>(db, ct),
            cancellationToken);

        logger.LogWarning("{Count} dead-lettered outbox message(s) of {Context} requeued by an operator", count, context);
        return count;
    }

    private enum RequeueOutcome { Requeued, NotFound, NotDeadLettered }

    private async Task<TResult> InScopeAsync<TResult>(string context, Guid? tenantId,
        Func<DbContext, Type, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        var contextType = registry.ContextTypes.FirstOrDefault(t => string.Equals(t.Name, context, StringComparison.OrdinalIgnoreCase))
                          ?? throw new NotFoundException("Outbox context", context);

        TenantInfo? tenant = null;
        if (tenantId is { } id && contextType.IsAssignableTo(typeof(ITenantDataContext)))
        {
            tenant = await tenantStore.FindByIdAsync(id, cancellationToken) ?? throw new NotFoundException("Tenant", id);
        }

        // Explicit, small cross-tenant scope: only this unit of work.
        using (tenant is null ? TenantScope.BeginHostScope() : TenantScope.Begin(tenant))
        {
            return await runner.RunAsync(async (services, ct) =>
            {
                var db = (DbContext)services.GetRequiredService(contextType);
                return await work(db, db.FindOutboxMessageType()!, ct);
            }, cancellationToken);
        }
    }

    private static Task<PaginatedResult<OutboxDeadLetterDto>> ListAsync<TMessage>(DbContext db, int? page, int? pageSize,
        CancellationToken ct) where TMessage : OutboxMessageBase =>
        db.Set<TMessage>().AsNoTracking()
            .Where(m => m.DeadLetteredAt != null)
            .OrderBy(m => m.DeadLetteredAt).ThenBy(m => m.Id)
            .Select(m => new OutboxDeadLetterDto(m.Id, m.MessageType, EF.Property<Guid?>(m, "TenantId"), m.OccurredOn,
                m.DeadLetteredAt, m.Attempts, m.LastError))
            .PagedResult(page, pageSize, ct);

    private async Task<RequeueOutcome> RequeueAsync<TMessage>(DbContext db, Guid id, CancellationToken ct)
        where TMessage : OutboxMessageBase
    {
        var updated = await Requeue(db.Set<TMessage>().Where(m => m.Id == id && m.DeadLetteredAt != null), ct);
        if (updated > 0)
            return RequeueOutcome.Requeued;
        return await db.Set<TMessage>().AnyAsync(m => m.Id == id, ct) ? RequeueOutcome.NotDeadLettered : RequeueOutcome.NotFound;
    }

    private async Task<int> RequeueAllAsync<TMessage>(DbContext db, CancellationToken ct) where TMessage : OutboxMessageBase =>
        await Requeue(db.Set<TMessage>().Where(m => m.DeadLetteredAt != null), ct);

    // LastError is kept for the record; the relay overwrites it on the next failure and clears it on success.
    private Task<int> Requeue<TMessage>(IQueryable<TMessage> rows, CancellationToken ct) where TMessage : OutboxMessageBase
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return rows.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.DeadLetteredAt, (DateTime?)null)
            .SetProperty(m => m.Attempts, 0)
            .SetProperty(m => m.NextAttemptAt, now)
            .SetProperty(m => m.LockedUntil, (DateTime?)null)
            .SetProperty(m => m.LockToken, (Guid?)null), ct);
    }
}
