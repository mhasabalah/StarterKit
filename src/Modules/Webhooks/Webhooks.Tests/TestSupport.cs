using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webhooks.Domains.Endpoints.Models;
using Webhooks.Options;
using Webhooks.Resources;
using Webhooks.Security;
using Webhooks.Services;

namespace Webhooks.Tests;

public static class TestSupport
{
    public static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    public static WebhookEndpoint NewEndpoint(string url = "https://hooks.example.com/in", string[]? events = null) =>
        WebhookEndpoint.Create(url, "test", events ?? ["catalog.product-published.v1"], "protected-secret");

    public static IOptions<WebhooksOptions> Options(Action<WebhooksOptions>? configure = null)
    {
        var options = new WebhooksOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static IStringLocalizer<WebhooksResource> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<WebhooksResource>(factory);
    }

    public static WebhookUrlPolicy UrlPolicy(Action<WebhooksOptions>? configure = null) => new(Options(configure));

    public static IWebhookEventCatalog Catalog() => new WebhookEventCatalog();
}

/// <summary>Temporarily switches CurrentUICulture (restored on dispose).</summary>
public sealed class UiCulture : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public UiCulture(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
