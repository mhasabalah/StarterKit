using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Extensions;
using Shared.Core.Mapster;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Infrastructure;
using Subscriptions.Infrastructure.Subscribers;
using Subscriptions.Options;

namespace Subscriptions;

public static class SubscriptionsModule
{
    public static IServiceCollection AddSubscriptionsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        PdfConfiguration.Configure();

        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IUpgradeTrustService, UpgradeTrustService>();
        services.AddScoped<IPaymentInstructionsRepository, PaymentInstructionsRepository>();
        services.AddScoped<IPaymentMethodRepository, PaymentMethodRepository>();

        // Default: subscribers are Identity users. Host apps can register their own
        // ISubscriberInfoProvider (e.g. for tenant/organization subscribers) before or after this call.
        services.TryAddScoped<ISubscriberInfoProvider, IdentityUserSubscriberInfoProvider>();

        services.Configure<SubscriptionOptions>(configuration.GetSection("Subscriptions"));

        services.AddMediatR(SubscriptionsRoot.AssemblyName);
        services.AddCustomMapster(SubscriptionsRoot.AssemblyName);
        services.AddCarterWithAssemblies(SubscriptionsRoot.AssemblyName);

        services.AddCustomDbContext<SubscriptionsDbContext>(configuration, "starterkit");
        services.AddHostedService<PlanSeedService>();

        services.AddTransient<ProcessPastDueSubscriptionsJob>();
        services.AddTransient<ProcessExpiredSubscriptionsJob>();
        services.AddTransient<ProcessRenewalRemindersJob>();
        services.AddTransient<ProcessInvoiceRetryJob>();
        services.AddTransient<ProcessUpgradeSlaExpiryJob>();
        services.AddTransient<ProcessScheduledDowngradesJob>();
        services.AddTransient<ProcessScheduledDowngradeRemindersJob>();
        services.AddTransient<ProcessPriceChangeNotificationsJob>();
        services.AddTransient<ProcessPlanPriceChangesJob>();
        services.AddTransient<ProcessScheduledCancellationsJob>();

        return services;
    }

    public static IApplicationBuilder UseSubscriptionsModule(this IApplicationBuilder app)
    {
        app.UseMigration<SubscriptionsDbContext>();

        using var scope = app.ApplicationServices.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<ProcessPastDueSubscriptionsJob>(
            "process-past-due",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessExpiredSubscriptionsJob>(
            "process-expired",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessRenewalRemindersJob>(
            "process-renewal-reminders",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 9 * * *");

        recurringJobManager.AddOrUpdate<ProcessInvoiceRetryJob>(
            "process-invoice-retry",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessUpgradeSlaExpiryJob>(
            "process-upgrade-sla-expiry",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessScheduledDowngradesJob>(
            "process-scheduled-downgrades",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessScheduledDowngradeRemindersJob>(
            "process-scheduled-downgrade-reminders",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 9 * * *");

        recurringJobManager.AddOrUpdate<ProcessPriceChangeNotificationsJob>(
            "process-price-change-notifications",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 9 * * *");

        recurringJobManager.AddOrUpdate<ProcessPlanPriceChangesJob>(
            "process-plan-price-changes",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        recurringJobManager.AddOrUpdate<ProcessScheduledCancellationsJob>(
            "process-scheduled-cancellations",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            "0 * * * *");

        return app;
    }
}
