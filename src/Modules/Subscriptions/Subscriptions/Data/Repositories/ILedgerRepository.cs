using Subscriptions.Domains.Ledger.Events;

namespace Subscriptions.Data.Repositories;

/// <summary>
/// Writes ledger entries. Append-only by design: there is deliberately no Update/Remove. The entry is added to the
/// caller's unit of work and saved by the caller's SaveChanges (same transaction as the money event).
/// Every entry also stages a <see cref="LedgerEntryRecordedEvent"/> on the same save, so it is dispatched only when
/// that save (or its transaction) commits and discarded when it fails.
/// Reads go through <see cref="SubscriptionsDbContext.LedgerEntries"/> (no-tracking projections in query handlers).
/// </summary>
public interface ILedgerRepository
{
    void Add(LedgerEntry entry);
}

public sealed class LedgerRepository(SubscriptionsDbContext dbContext, IDomainEventCollector domainEvents) : ILedgerRepository
{
    public void Add(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        dbContext.LedgerEntries.Add(entry);

        // LedgerEntry is an insert-only row, not an aggregate, so it cannot carry events itself. Staging against the
        // context gives the same guarantee: the next save of this context commits or discards it with the entry.
        domainEvents.Stage(dbContext, [new LedgerEntryRecordedEvent(entry.Id, entry.SubscriptionId)]);
    }
}
