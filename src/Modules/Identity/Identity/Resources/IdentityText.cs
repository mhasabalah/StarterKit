using System.Globalization;

namespace Identity.Resources;

/// <summary>
/// Localized text for places without DI - typed exceptions and aggregate invariants - read from the same
/// <c>IdentityResource</c> resx files (en + ar) that <c>IStringLocalizer&lt;IdentityResource&gt;</c> uses. The culture is
/// the request culture (<see cref="CultureInfo.CurrentUICulture"/>, set by request localization), so a message
/// thrown while handling an Arabic request is Arabic. A missing key falls back to the key itself, never throws.
/// </summary>
public static class IdentityText
{
    public static string Get(string key, params object?[] args)
    {
        var format = IdentityResource.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
