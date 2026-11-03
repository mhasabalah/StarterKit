namespace Subscriptions.Data.Repositories;

/// <summary>
/// Writes ledger entries. Append-only by design: there is deliberately no Update/Remove. The entry is added to the
/// caller's unit of work and saved by the caller's SaveChanges (same transaction as the money event).
/// Reads go through <see cref="SubscriptionsDbContext.LedgerEntries"/> (no-tracking projections in query handlers).
/// </summary>
public interface ILedgerRepository
{
    void Add(LedgerEntry entry);
}

public sealed class LedgerRepository(SubscriptionsDbContext dbContext) : ILedgerRepository
{
    public void Add(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        dbContext.LedgerEntries.Add(entry);
    }
}
