using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Security;
using Webhooks.Services;

namespace Webhooks;

/// <summary>
/// Outbound webhooks (tenant-data module). Program.cs must also pass <c>WebhooksRoot.AssemblyName</c> to
/// <c>AddMassTransitWithAssemblies</c> (the forwarding consumers) and <c>WebhooksContractsRoot.AssemblyName</c> to
/// <c>AddEnumLookup</c> (<c>WebhookDeliveryStatus</c>). See wiki/Modules/Webhooks.md.
/// </summary>
public static class WebhooksModule
{
    public const string DispatchSweepJobId = "webhooks-dispatch-due";
    public const string PurgeJobId = "webhooks-purge-deliveries";

    public static IServiceCollection AddWebhooksModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebhooksOptions>(configuration.GetSection(WebhooksOptions.SectionName));
        services.AddSingleton<IValidateOptions<WebhooksOptions>, WebhooksOptionsValidator>();
        services.AddOptions<WebhooksOptions>().ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection(); // purpose "Webhooks.EndpointSecret"; the host configures the key ring

        // One repository per aggregate root (deliveries are children of the delivery log, not of the endpoint).
        services.AddScoped<IWebhookEndpointRepository, WebhookEndpointRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();

        services.AddSingleton<IWebhookUrlPolicy, WebhookUrlPolicy>();
        services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtector>();
        services.AddSingleton<IWebhookEventCatalog, WebhookEventCatalog>();
        services.AddSingleton<IWebhookSender, WebhookHttpSender>();
        services.AddScoped<IWebhookFanOut, WebhookFanOut>();

        services.AddMediator(WebhooksRoot.AssemblyName);
        services.AddCarterWithAssemblies(WebhooksRoot.AssemblyName);
        services.AddCustomDbContext<WebhooksDbContext>(configuration, "starterkit");

        // Hangfire jobs
        services.AddTransient<DeliverWebhookJob>();
        services.AddTransient<DispatchDueWebhooksJob>();
        services.AddTransient<PurgeWebhookDeliveriesJob>();

        return services;
    }

    public static IApplicationBuilder UseWebhooksModule(this IApplicationBuilder app)
    {
        app.UseMigration<WebhooksDbContext>();

        using var scope = app.ApplicationServices.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<WebhooksOptions>>().Value;
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<DispatchDueWebhooksJob>(
            DispatchSweepJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.DispatchSweepCron);

        recurringJobManager.AddOrUpdate<PurgeWebhookDeliveriesJob>(
            PurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.PurgeCron);

        return app;
    }
}

/// <summary>
/// Production guard for the SSRF switches: plain http and private-network targets are development conveniences. Outside
/// Development / Testing the application refuses to start with them on. Without an <see cref="IHostEnvironment"/> (unit
/// tests) nothing is enforced.
/// </summary>
public sealed class WebhooksOptionsValidator(IServiceProvider services) : IValidateOptions<WebhooksOptions>
{
    private static readonly string[] Relaxed = ["Development", "Testing", "Test", "E2E"];

    public ValidateOptionsResult Validate(string? name, WebhooksOptions options)
    {
        var failures = new List<string>();
        if (options.MaxAttempts < 1) failures.Add("Webhooks:MaxAttempts must be at least 1.");
        if (options.RequestTimeoutSeconds < 1) failures.Add("Webhooks:RequestTimeoutSeconds must be at least 1.");
        if (options.MaxResponseBytes < 1) failures.Add("Webhooks:MaxResponseBytes must be at least 1.");

        var environment = services.GetService<IHostEnvironment>();
        if (environment is not null && !Relaxed.Contains(environment.EnvironmentName, StringComparer.OrdinalIgnoreCase))
        {
            if (options.AllowPrivateNetworks)
                failures.Add($"Webhooks:AllowPrivateNetworks must be false in '{environment.EnvironmentName}' (SSRF guard).");
            if (!options.RequireHttps)
                failures.Add($"Webhooks:RequireHttps must be true in '{environment.EnvironmentName}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
