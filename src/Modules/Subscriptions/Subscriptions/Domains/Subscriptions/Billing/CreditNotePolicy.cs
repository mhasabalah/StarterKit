namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// Limits and amounts of credit notes. An invoice can be credited, in one or more notes, up to its gross total.
/// The note that closes the remainder takes exactly the remaining net/tax so credited parts always sum to the original.
/// </summary>
public static class CreditNotePolicy
{
    public static decimal Creditable(decimal originalGross, decimal creditedGross) =>
        Math.Max(0m, originalGross - creditedGross);

    /// <summary>
    /// Returns the credit note breakdown (NEGATIVE amounts).
    /// </summary>
    /// <param name="original">The original invoice's issue-time breakdown (positive).</param>
    /// <param name="credited">Sum of earlier credit notes as POSITIVE values (net, tax, gross).</param>
    /// <param name="requestedGross">Gross to credit (positive); null = everything still creditable.</param>
    public static TaxBreakdown Compute(TaxBreakdown original, TaxBreakdown credited, decimal? requestedGross)
    {
        if (original.Gross <= 0m)
        {
            throw new ConflictException("Only invoices with a positive total can be credited.");
        }

        var remaining = Creditable(original.Gross, credited.Gross);
        if (remaining <= 0m)
        {
            throw new ConflictException("The invoice has already been fully credited.");
        }

        var amount = requestedGross ?? remaining;
        if (amount <= 0m || TaxCalculator.Round(amount) != amount)
        {
            throw new BadRequestException("The credit amount must be positive with at most 2 decimals.");
        }

        if (amount > remaining)
        {
            throw new BadRequestException($"The credit amount exceeds the creditable remainder ({remaining:F2}).");
        }

        if (amount == remaining)
        {
            var net = original.Net - credited.Net;
            return new TaxBreakdown(-net, -(amount - net), -amount, original.Rate, original.PricesIncludeTax);
        }

        var partial = TaxCalculator.FromGross(amount, new TaxPolicy(original.Rate, original.PricesIncludeTax));
        return partial.Negate();
    }
}
