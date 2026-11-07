namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>Inputs for a refund quote. All money is gross (what was actually received).</summary>
/// <param name="PeriodPaid">Money received for the current period (activation/renewal payment + paid prorations).</param>
/// <param name="Refundable">Σ paid − Σ approved/processed refunds for the subscription (the hard cap).</param>
/// <param name="PeriodStart">Start of the current billing period.</param>
/// <param name="PeriodEnd">End of the current billing period (renewal date).</param>
/// <param name="ServiceEnd">When service actually ended (subscription EndDate, else CancelledAt).</param>
/// <param name="Currency">Decides the rounding decimals of a prorated amount.</param>
public sealed record RefundInput(
    decimal PeriodPaid,
    decimal Refundable,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset ServiceEnd,
    int FullWindowDays,
    int ProratedWindowDays,
    string Currency);

public sealed record RefundQuote(RefundType Type, decimal Amount);

/// <summary>
/// The ONE refund rule (previously duplicated in RequestRefund and the scheduled-cancellation job).
/// <list type="bullet">
/// <item>refundable = Σ paid − Σ (approved + processed) refunds; nothing refundable → rejected.</item>
/// <item>Service ended within the full window of the period start → full refund of the period's money.</item>
/// <item>Within the prorated window → period money × unused days / actual period days (decimal day math).</item>
/// <item>The result is always capped at the refundable amount.</item>
/// </list>
/// </summary>
public static class RefundPolicy
{
    public static decimal Refundable(IEnumerable<decimal> paidAmounts, IEnumerable<decimal> refundedAmounts) =>
        Math.Max(0m, paidAmounts.Sum() - refundedAmounts.Sum());

    public static RefundQuote Quote(RefundInput input)
    {
        if (input.Refundable <= 0m)
        {
            throw new ConflictException("Nothing is refundable for this subscription.");
        }

        if (input.ServiceEnd > input.PeriodStart.AddDays(input.ProratedWindowDays))
        {
            throw new ConflictException("The refund window for this subscription has expired.");
        }

        RefundType type;
        decimal amount;

        if (input.ServiceEnd <= input.PeriodStart.AddDays(input.FullWindowDays))
        {
            type = RefundType.Full;
            amount = input.PeriodPaid;
        }
        else
        {
            type = RefundType.Prorated;
            var unused = BillingPeriod.RemainingFraction(input.PeriodStart, input.PeriodEnd, input.ServiceEnd);
            amount = Currency.Round(input.PeriodPaid * unused, input.Currency);
        }

        amount = Math.Min(amount, input.Refundable);
        if (amount <= 0m)
        {
            throw new ConflictException("Calculated refund amount is zero.");
        }

        return new RefundQuote(type, amount);
    }
}
