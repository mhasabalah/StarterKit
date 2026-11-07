using System.Globalization;

namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// ISO 4217 currency knowledge, the ONE place that decides how many decimals an amount of a currency has: 0 (JPY, KRW),
/// 2 (USD, SAR, EGP, the default), 3 (KWD, BHD, OMR, JOD, TND, IQD, LYD) or 4 (CLF). Rounding, scale validation,
/// provider minor units and display all go through here. Callers must name the currency; an unknown code falls back
/// to 2 decimals (validators reject unknown codes before they reach an amount).
/// </summary>
internal static class Currency
{
    public const int DefaultExponent = 2;

    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    private static readonly HashSet<string> FourDecimal = new(StringComparer.OrdinalIgnoreCase) { "CLF", "UYW" };

    // Active ISO 4217 codes. CLF and UYW are ISO fund codes; they are kept on purpose (they have 4 decimals, which
    // the numeric(19,4) columns support). Precious-metal, testing and other fund codes are not billable and left out.
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD", "BDT", "BGN", "BHD", "BIF",
        "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD", "CAD", "CDF", "CHF", "CLF", "CLP", "CNY", "COP",
        "CRC", "CUP", "CVE", "CZK", "DJF", "DKK", "DOP", "DZD", "EGP", "ERN", "ETB", "EUR", "FJD", "FKP", "GBP", "GEL",
        "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD", "HKD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY", "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD",
        "LSL", "LYD", "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD", "OMR", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG", "QAR", "RON",
        "RSD", "RUB", "RWF", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC",
        "SYP", "SZL", "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "UGX", "USD", "UYU", "UYW",
        "UZS", "VED", "VES", "VND", "VUV", "WST", "XAF", "XCD", "XCG", "XOF", "XPF", "YER", "ZAR", "ZMW", "ZWG"
    };

    /// <summary>True for a 3-letter ISO 4217 code of a billable currency (case-insensitive).</summary>
    public static bool IsKnown(string? code) => code is { Length: 3 } && Known.Contains(code);

    /// <summary>Decimals of the currency's minor unit: 0, 2, 3 or 4 (2 for null / unknown).</summary>
    public static int Exponent(string? code) =>
        code is null ? DefaultExponent
        : ZeroDecimal.Contains(code) ? 0
        : ThreeDecimal.Contains(code) ? 3
        : FourDecimal.Contains(code) ? 4
        : DefaultExponent;

    /// <summary>The smallest billable amount of the currency: 0.01 for USD, 1 for JPY, 0.001 for KWD.</summary>
    public static decimal SmallestUnit(string? code) => 1m / Pow10(Exponent(code));

    /// <summary>Rounds to the currency's decimals, midpoints away from zero (the one rounding rule of billing).</summary>
    public static decimal Round(decimal amount, string? code) =>
        decimal.Round(amount, Exponent(code), MidpointRounding.AwayFromZero);

    /// <summary>True when the amount has no more decimals than the currency allows (10.001 is not a valid USD amount).</summary>
    public static bool HasValidScale(decimal amount, string? code) => decimal.Round(amount, Exponent(code)) == amount;

    public static long ToMinorUnits(decimal amount, string? code) =>
        (long)decimal.Round(amount * Pow10(Exponent(code)), 0, MidpointRounding.AwayFromZero);

    public static decimal FromMinorUnits(long minor, string? code) =>
        decimal.Round(minor / Pow10(Exponent(code)), Exponent(code), MidpointRounding.AwayFromZero);

    /// <summary>The amount with the currency's decimals, without the code: "10.50", "1000", "12.345".</summary>
    public static string Format(decimal amount, string? code) => FormatLocalized(amount, code, CultureInfo.InvariantCulture);

    /// <summary>Same, with the digits of <paramref name="culture"/> (decimal separator, numerals).</summary>
    public static string FormatLocalized(decimal amount, string? code, IFormatProvider culture) =>
        Round(amount, code).ToString("F" + Exponent(code), culture);

    /// <summary>The amount followed by its code: "10.50 SAR".</summary>
    public static string FormatWithCode(decimal amount, string? code) =>
        FormatLocalizedWithCode(amount, code, CultureInfo.InvariantCulture);

    public static string FormatLocalizedWithCode(decimal amount, string? code, IFormatProvider culture) =>
        $"{FormatLocalized(amount, code, culture)} {code?.ToUpperInvariant()}".TrimEnd();

    private static decimal Pow10(int exponent) => exponent switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1000m,
        _ => 10000m
    };
}
