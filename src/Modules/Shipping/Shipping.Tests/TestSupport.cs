using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shipping.Resources;

namespace Shipping.Tests;

public static class TestSupport
{
    /// <summary>The real resx-backed localizer (en by default), so validators are tested with their real messages.</summary>
    public static IStringLocalizer<ShippingResource> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<ShippingResource>(factory);
    }
}
