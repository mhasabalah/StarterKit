namespace Subscriptions.Domains.Ledger.Events;

/// <summary>
/// A money movement was booked. Raised by <c>ILedgerRepository.Add</c> (the only ledger write) and dispatched only
/// when the save or transaction that persists the entry commits.
/// </summary>
public record LedgerEntryRecordedEvent(Guid LedgerEntryId, Guid SubscriptionId) : IDomainEvent;
