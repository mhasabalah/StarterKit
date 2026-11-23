using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Search.Resources;

namespace Search.Tests;

public static class TestSupport
{
    /// <summary>The real resx-backed localizer (en by default), so validators are tested with their real messages.</summary>
    public static IStringLocalizer<SearchResource> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<SearchResource>(factory);
    }
}
