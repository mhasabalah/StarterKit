using System.Globalization;

namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// The tax configuration in force when an amount is computed
/// (<c>Subscriptions:Tax:Rate</c> / <c>Subscriptions:Tax:PricesIncludeTax</c>).
/// </summary>
/// <param name="Rate">Decimal fraction, e.g. 0.15 for 15% or 0.075 for 7.5%. Must be within [0, 1].</param>
/// <param name="PricesIncludeTax">True when plan prices are gross (tax-inclusive); false when they are net.</param>
public readonly record struct TaxPolicy(decimal Rate, bool PricesIncludeTax);

/// <summary>Net + tax = gross, always exactly (the parts are derived so they sum).</summary>
public readonly record struct TaxBreakdown(decimal Net, decimal Tax, decimal Gross, decimal Rate, bool PricesIncludeTax)
{
    public TaxBreakdown Negate() => this with { Net = -Net, Tax = -Tax, Gross = -Gross };
}

/// <summary>
/// The ONE place tax is computed. Pure and deterministic; amounts are rounded to the currency's decimals
/// (2 when no currency is given; <see cref="MidpointRounding.AwayFromZero"/>) and the parts always sum exactly to the gross.
/// <list type="bullet">
/// <item>Inclusive: net = round(gross / (1 + r)), tax = gross - net.</item>
/// <item>Exclusive: net = price, tax = round(net * r), gross = net + tax.</item>
/// </list>
/// </summary>
public static class TaxCalculator
{
    /// <summary>Breakdown of a PRICE (gross when prices include tax, net otherwise).</summary>
    public static TaxBreakdown FromPrice(decimal price, TaxPolicy policy, string? currency = null)
    {
        Validate(policy);

        if (policy.PricesIncludeTax)
        {
            return FromGross(price, policy, currency);
        }

        var net = Round(price, currency);
        var tax = Round(net * policy.Rate, currency);
        return new TaxBreakdown(net, tax, net + tax, policy.Rate, policy.PricesIncludeTax);
    }

    /// <summary>
    /// Breakdown of money actually charged (always gross). Used for invoices so the document matches the payment;
    /// for an exclusive price p this recovers exactly net = p (rounding error of the tax is below half a cent).
    /// </summary>
    public static TaxBreakdown FromGross(decimal gross, TaxPolicy policy, string? currency = null)
    {
        Validate(policy);

        var roundedGross = Round(gross, currency);
        var net = Round(roundedGross / (1m + policy.Rate), currency);
        return new TaxBreakdown(net, roundedGross - net, roundedGross, policy.Rate, policy.PricesIncludeTax);
    }

    /// <summary>Gross amount due for a price.</summary>
    public static decimal AmountDue(decimal price, TaxPolicy policy, string? currency = null) =>
        FromPrice(price, policy, currency).Gross;

    /// <summary>Formats a rate as a percent without truncation: 0.15 → "15%", 0.075 → "7.5%".</summary>
    public static string FormatRate(decimal rate) =>
        (rate * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    /// <summary>Rounds to the currency's decimals (2 when <paramref name="currency"/> is null).</summary>
    public static decimal Round(decimal value, string? currency = null) => Currency.Round(value, currency);

    private static void Validate(TaxPolicy policy)
    {
        if (policy.Rate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy.Rate, "Tax rate must be a fraction between 0 and 1.");
        }
    }
}
