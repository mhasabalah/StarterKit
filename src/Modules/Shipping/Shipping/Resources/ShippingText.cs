using System.Globalization;
using System.Resources;

namespace Shipping.Resources;

/// <summary>
/// Localized module text without dependency injection (exceptions, aggregate invariants), from the same
/// ShippingResource.resx / .ar.resx, in <see cref="CultureInfo.CurrentUICulture"/>.
/// </summary>
public static class ShippingText
{
    private static readonly ResourceManager Resources = new("Shipping.Resources.ShippingResource", typeof(ShippingText).Assembly);

    public static string Get(string key, params object?[] args)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
