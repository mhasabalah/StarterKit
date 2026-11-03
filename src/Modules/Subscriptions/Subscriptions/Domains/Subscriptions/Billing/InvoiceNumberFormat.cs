namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>Invoice / credit note numbers: <c>{Prefix}-{yyyy}-{seq:D6}</c>, e.g. <c>INV-2026-000042</c>.</summary>
public static class InvoiceNumberFormat
{
    /// <summary>Column length of <c>invoice_number</c>.</summary>
    public const int MaxLength = 40;

    /// <summary>Longest allowed prefix so a number always fits <see cref="MaxLength"/> (prefix + "-yyyy-" + up to 10 digits).</summary>
    public const int MaxPrefixLength = 20;

    public static string Format(string prefix, int year, long sequence)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > MaxPrefixLength)
        {
            throw new ArgumentException($"Invoice number prefix must be 1-{MaxPrefixLength} characters.", nameof(prefix));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2000);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1L);

        return $"{prefix.Trim()}-{year:D4}-{sequence:D6}";
    }
}
