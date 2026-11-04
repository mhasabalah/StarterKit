using Microsoft.EntityFrameworkCore.Storage;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public class InboundWebhookEventRepository(SubscriptionsDbContext context) : IInboundWebhookEventRepository
{
    public async Task<bool> TryInsertAsync(
        Guid id, string provider, PaymentWebhookEvent webhookEvent, string rawBody, DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        var type = webhookEvent.Type.ToString();
        var status = InboundWebhookStatus.Pending.ToString();

        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO subscriptions.inbound_webhook_events
                (id, provider, event_id, type, provider_reference, amount, currency, payload, status, received_at, attempts)
            VALUES
                ({id}, {provider}, {webhookEvent.EventId}, {type}, {webhookEvent.ProviderReference},
                 {webhookEvent.Amount}, {webhookEvent.Currency}, {rawBody}::jsonb, {status}, {receivedAt}, 0)
            ON CONFLICT (provider, event_id) DO NOTHING
            """, ct);

        return inserted == 1;
    }

    public async Task<(Guid Id, InboundWebhookStatus Status)?> FindAsync(
        string provider, string eventId, CancellationToken ct = default)
    {
        var row = await context.InboundWebhookEvents.AsNoTracking()
            .Where(x => x.Provider == provider && x.EventId == eventId)
            .Select(x => new { x.Id, x.Status })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.Id, row.Status);
    }

    public Task<InboundWebhookEvent?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.InboundWebhookEvents.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => context.Database.BeginTransactionAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

    public void ClearTracking() => context.ChangeTracker.Clear();
}
