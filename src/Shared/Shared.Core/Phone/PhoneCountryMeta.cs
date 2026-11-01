namespace Shared.Core.Phone;

/// <summary>
/// Static dial-code reference for UI hints. Validation always goes through
/// <see cref="IPhoneValidationService"/> — this dictionary is display-only.
/// </summary>
public static class PhoneCountryMeta
{
    public static IReadOnlyDictionary<string, string> DialCodes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EG"] = "+20",
            ["SA"] = "+966",
            ["AE"] = "+971",
            ["KW"] = "+965",
            ["QA"] = "+974",
            ["BH"] = "+973",
            ["OM"] = "+968",
            ["JO"] = "+962",
            ["LB"] = "+961",
            ["IQ"] = "+964",
            ["SY"] = "+963",
            ["YE"] = "+967",
            ["LY"] = "+218",
            ["TN"] = "+216",
            ["DZ"] = "+213",
            ["MA"] = "+212",
            ["SD"] = "+249",
            ["SO"] = "+252",
            ["PS"] = "+970",
            ["TR"] = "+90",
            ["IR"] = "+98",
            ["PK"] = "+92",
            ["IN"] = "+91",
            ["US"] = "+1",
            ["GB"] = "+44",
            ["DE"] = "+49",
            ["FR"] = "+33",
            ["IT"] = "+39",
            ["ES"] = "+34",
            ["RU"] = "+7",
            ["CN"] = "+86",
            ["JP"] = "+81",
            ["BR"] = "+55",
            ["CA"] = "+1",
            ["AU"] = "+61",
        };

    /// <summary>Returns the dial code prefix for <paramref name="countryCode"/>, or null.</summary>
    public static string? GetDialCode(string countryCode)
        => DialCodes.TryGetValue(countryCode, out var code) ? code : null;
}
