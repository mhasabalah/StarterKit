using Subscriptions.Contracts.Ledger.DTOs;
using Subscriptions.Contracts.Ledger.Enums;

namespace Subscriptions.Contracts.Ledger.Queries;

/// <summary>Paged ledger (<c>GET admin/billing/ledger</c>). Range is [From, To) on OccurredAt (UTC).</summary>
public record GetLedgerEntriesQuery : BaseQuery, IQuery<PaginatedResult<LedgerEntryDto>>
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public LedgerEntryType? Type { get; init; }
    public Guid? SubscriberId { get; init; }
    public Guid? SubscriptionId { get; init; }
    public string? Currency { get; init; }
}
