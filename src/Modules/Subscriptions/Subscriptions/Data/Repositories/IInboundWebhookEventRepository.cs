using Microsoft.EntityFrameworkCore.Storage;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public interface IInboundWebhookEventRepository
{
    /// <summary>
    /// Insert-or-ignore by <c>(provider, event_id)</c> (<c>ON CONFLICT DO NOTHING</c>): returns true when the row was
    /// created, false for a duplicate delivery.
    /// </summary>
    Task<bool> TryInsertAsync(
        Guid id, string provider, PaymentWebhookEvent webhookEvent, string rawBody, DateTimeOffset receivedAt,
        CancellationToken ct = default);

    /// <summary>Id and status of an already stored event, or null.</summary>
    Task<(Guid Id, InboundWebhookStatus Status)?> FindAsync(string provider, string eventId, CancellationToken ct = default);

    Task<InboundWebhookEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Drops every tracked entity (after a rolled-back unit of work).</summary>
    void ClearTracking();
}
