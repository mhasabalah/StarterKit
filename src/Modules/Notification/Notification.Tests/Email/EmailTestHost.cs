using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Notification.Tests.Email;

/// <summary>Builds the real renderer / registry / sample factory exactly as production wires them.</summary>
internal sealed class EmailTestHost
{
    public const string AppBaseUrl = "https://app.starterkit.test";

    public EmailOptions Options { get; }
    public FluidTemplateRenderer Renderer { get; }
    public EmailTemplateRegistry Registry { get; }
    public EmailTemplateSamples Samples { get; }

    public EmailTestHost(Action<EmailOptions>? configure = null)
    {
        Options = new EmailOptions
        {
            BrandName = "StarterKit",
            AppBaseUrl = AppBaseUrl,
            SupportEmail = "support@starterkit.test",
            AllowedLinkHosts = ["cdn.starterkit.test"]
        };
        configure?.Invoke(Options);

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<EmailOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
        foreach (var type in typeof(EmailOptions).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(IEmailTemplate).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(IEmailTemplate), type);
        }
        services.AddSingleton<EmailTemplateRegistry>();
        services.AddSingleton<EmailTemplateSamples>();
        services.AddSingleton<FluidTemplateRenderer>();
        var provider = services.BuildServiceProvider();

        Renderer = provider.GetRequiredService<FluidTemplateRenderer>();
        Registry = provider.GetRequiredService<EmailTemplateRegistry>();
        Samples = provider.GetRequiredService<EmailTemplateSamples>();
    }

    public Task<string> RenderAsync(EmailTemplate template, NotificationCulture culture, Func<string, string?>? valueFor = null)
        => Renderer.RenderAsync(
            FluidTemplateRenderer.GetTemplateFileName(template),
            Samples.Build(template, valueFor),
            culture);

    public static TheoryData<EmailTemplate, NotificationCulture> AllTemplatesAndCultures()
    {
        var data = new TheoryData<EmailTemplate, NotificationCulture>();
        foreach (var t in Enum.GetValues<EmailTemplate>())
        foreach (var c in Enum.GetValues<NotificationCulture>())
            data.Add(t, c);
        return data;
    }

    /// <summary>Templates whose model has at least one *Url property (they render a Button).</summary>
    public static TheoryData<EmailTemplate> TemplatesWithLinks()
    {
        var host = new EmailTestHost();
        var data = new TheoryData<EmailTemplate>();
        foreach (var t in Enum.GetValues<EmailTemplate>())
        {
            var modelType = host.Registry.GetModelType(t)!;
            if (modelType.GetProperties().Any(p => p.Name.EndsWith("Url", StringComparison.Ordinal)))
                data.Add(t);
        }
        return data;
    }
}
