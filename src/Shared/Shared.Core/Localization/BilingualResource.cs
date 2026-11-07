using System.Globalization;
using System.Resources;
using Shared.Contracts.Localization;

namespace Shared.Core.Localization;

/// <summary>
/// Reads one resource key in BOTH languages from a module's resx pair (neutral = English, <c>.ar</c> = Arabic), for
/// payloads that store text for every language and pick one at delivery time (in-app notifications:
/// <see cref="LocalizedText"/>). A missing Arabic entry falls back to English; a missing key yields the key.
/// </summary>
public static class BilingualResource
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");

    public static LocalizedText Get(ResourceManager resources, string key, params object?[] args) =>
        new(Format(resources, key, English, args), Format(resources, key, Arabic, args));

    private static string Format(ResourceManager resources, string key, CultureInfo culture, object?[] args)
    {
        var format = resources.GetString(key, culture) ?? key;
        return args.Length == 0 ? format : string.Format(culture, format, args);
    }
}
