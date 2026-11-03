namespace Subscriptions.Contracts.Ledger.Enums;

/// <summary>Kind of money movement in the billing ledger. Stored by name; numbers are stable.</summary>
public enum LedgerEntryType
{
    /// <summary>Money received (approved proof, manual/partial receipt). Amount &gt; 0.</summary>
    Payment = 0,

    /// <summary>Money paid back to the subscriber. Amount &lt; 0.</summary>
    Refund = 1,

    /// <summary>A standalone credit note (invoice correction without a refund). Amount &lt; 0.</summary>
    CreditNote = 2,

    /// <summary>Manual adjustment, e.g. a complimentary activation (amount 0). Any sign.</summary>
    Adjustment = 3
}
