using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Agentic.Resources;

namespace Agentic.Tests;

public static class TestSupport
{
    /// <summary>The real resx-backed localizer (en by default), so validators are tested with their real messages.</summary>
    public static IStringLocalizer<AgenticResource> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<AgenticResource>(factory);
    }
}
