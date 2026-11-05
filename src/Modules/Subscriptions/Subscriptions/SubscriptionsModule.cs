using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Extensions;
using Shared.Core.Mapster;
using Shared.Core.Messaging.Outbox;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.Metering;
using Subscriptions.Domains.Metering.Jobs;
using Subscriptions.Domains.Subscriptions.Entitlements;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Jobs;
using Subscriptions.Domains.OnlinePayments.Services;
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
        PdfConfiguration.Configure(configuration["Subscriptions:Pdf:License"]);

        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IEntitlementCache, EntitlementCache>();
        services.AddScoped<PlanEntitlementService>();
        services.AddScoped<IEntitlementService>(sp => sp.GetRequiredService<PlanEntitlementService>()); // replaces the permissive Shared.Core default
        services.AddScoped<IEntitlementSnapshotSource>(sp => sp.GetRequiredService<PlanEntitlementService>());

        // Usage metering + quotas: replace the permissive Shared.Core defaults. Counters live in Redis when it is
        // enabled (INCRBY), otherwise in-process; the flush job persists them to usage_records.
        services.AddSingleton<UsageMetrics>();
        services.AddSingleton<IUsageCounterStore>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } redis
                ? new RedisUsageCounterStore(redis, sp.GetRequiredService<IOptions<Shared.Core.Options.RedisOptions>>())
                : new InMemoryUsageCounterStore());
        services.AddScoped<UsageService>();
        services.AddScoped<IUsageMeter>(sp => sp.GetRequiredService<UsageService>());
        services.AddScoped<IQuotaService>(sp => sp.GetRequiredService<UsageService>());
        services.AddSingleton<IUsageFlusher, UsageFlusher>();
        services.AddSingleton<ITrialReminderProcessor, TrialReminderProcessor>();
        services.AddSingleton<IDunningProcessor, DunningProcessor>();
        services.AddScoped<IInvoiceIssuer, InvoiceIssuer>();
        services.AddScoped<IUpgradeTrustService, UpgradeTrustService>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<IProofSubmissionService, ProofSubmissionService>();
        services.AddSingleton<IRenewalOrchestrator, RenewalOrchestrator>();
        services.AddScoped<IPaymentInstructionsRepository, PaymentInstructionsRepository>();
        services.AddScoped<IPaymentMethodRepository, PaymentMethodRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IOnlinePaymentSessionRepository, OnlinePaymentSessionRepository>();
        services.AddScoped<IOnlinePaymentRefundRepository, OnlinePaymentRefundRepository>();
        services.AddScoped<IInboundWebhookEventRepository, InboundWebhookEventRepository>();
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IComplimentaryActivationService, ComplimentaryActivationService>();
        services.AddScoped<IAdminNotificationRecipients, AdminNotificationRecipients>();
        services.TryAddSingleton(TimeProvider.System);

        // Default: subscribers are Identity users. Host apps can register their own
        // ISubscriberInfoProvider (e.g. for tenant/organization subscribers) before or after this call.
        services.TryAddScoped<ISubscriberInfoProvider, UserDirectorySubscriberInfoProvider>();

        // Default: the subscriber is the signed-in user. The Tenancy module registers the tenant-based provider
        // (subscriber = current tenant) when multi-tenancy is enabled.
        services.TryAddScoped<ISubscriberIdProvider, CurrentUserSubscriberIdProvider>();

        services.AddOptions<SubscriptionOptions>()
            .Bind(configuration.GetSection("Subscriptions"))
            .Validate(o => o.Tax.Rate is >= 0m and <= 1m, "Subscriptions:Tax:Rate must be a fraction between 0 and 1 (e.g. 0.15).")
            .Validate(o => o.Entitlements.CacheSeconds > 0, "Subscriptions:Entitlements:CacheSeconds must be positive.")
            .Validate(o => o.ReactivationWindowDays >= 0, "Subscriptions:ReactivationWindowDays must not be negative.")
            .Validate(o => o.AdminNotifications.DigestStaleProofHours > 0,
                "Subscriptions:AdminNotifications:DigestStaleProofHours must be positive.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Invoice.NumberPrefix) &&
                           o.Invoice.NumberPrefix.Length <= Domains.Subscriptions.Billing.InvoiceNumberFormat.MaxPrefixLength,
                "Subscriptions:Invoice:NumberPrefix must be 1-20 characters.")
            .Validate(o => o.PaymentGateways.All(g => !g.Value.Enabled || !string.IsNullOrWhiteSpace(g.Value.Secret)),
                "Subscriptions:PaymentGateways:<Name>:Secret is required when the gateway is enabled.")
            .Validate(o => o.PaymentGateways.All(g => !g.Value.Enabled || !g.Key.Equals("Stripe", StringComparison.OrdinalIgnoreCase) ||
                                                     !string.IsNullOrWhiteSpace(g.Value.ApiKey)),
                "Subscriptions:PaymentGateways:Stripe:ApiKey is required when Stripe is enabled.")
            .Validate(o => o.PaymentGateways.All(g => !g.Value.Enabled || !g.Key.Equals("Paymob", StringComparison.OrdinalIgnoreCase) ||
                                                     (!string.IsNullOrWhiteSpace(g.Value.ApiKey) &&
                                                      !string.IsNullOrWhiteSpace(g.Value.PublicKey) && g.Value.IntegrationIds.Length > 0)),
                "Subscriptions:PaymentGateways:Paymob needs ApiKey, PublicKey and IntegrationIds when enabled.")
            .Validate(o => o.Trials.ReminderDaysBeforeEnd >= 0 && o.Trials.PostTrialGraceDays >= 0,
                "Subscriptions:Trials values must not be negative.")
            .Validate(o => o.Dunning.EffectiveScheduleDays.All(d => d > 0) &&
                           o.Dunning.EffectiveScheduleDays.Zip(o.Dunning.EffectiveScheduleDays.Skip(1), (a, b) => a < b).All(x => x) &&
                           o.Dunning.FinalNoticeDaysBeforeGraceEnd >= 0,
                "Subscriptions:Dunning:ScheduleDays must be positive, strictly ascending day numbers.")
            .Validate(o => o.Metering.MonthlyCounterTtlDays > 0, "Subscriptions:Metering:MonthlyCounterTtlDays must be positive.")
            .Validate(o => o.PaymentWebhooks.MaxAttempts > 0 && o.PaymentWebhooks.RetryBackoffMinutes >= 0,
                "Subscriptions:PaymentWebhooks:MaxAttempts must be positive.")
            .ValidateOnStart();

        services.AddScoped<IPaymentGateway, FakePaymentGateway>(); // template adapter; real providers register next to it
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<IPaymentGateway, PaymobPaymentGateway>();
        // Typed clients: timeouts + circuit breaker only; a retried POST could double-charge / double-refund.
        services.AddHttpClient(StripePaymentGateway.HttpClientName)
            .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => ValueTask.FromResult(false));
        services.AddHttpClient(PaymobPaymentGateway.HttpClientName)
            .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => ValueTask.FromResult(false));
        services.AddScoped<IPaymentGatewayRegistry, PaymentGatewayRegistry>();
        services.AddScoped<IInboundWebhookProcessor, InboundWebhookProcessor>();
        services.AddScoped<IRefundBooking, RefundBooking>();

        services.AddMediator(SubscriptionsRoot.AssemblyName);
        services.AddCustomMapster(SubscriptionsRoot.AssemblyName);
        services.AddCarterWithAssemblies(SubscriptionsRoot.AssemblyName);

        services.AddCustomDbContext<SubscriptionsDbContext>(configuration, "starterkit");
        services.AddOutbox<SubscriptionsDbContext>(); // delivers the integration events raised by Subscriptions aggregates
        services.AddHostedService<PlanSeedService>();

        services.AddTransient<ProcessPastDueSubscriptionsJob>();
        services.AddTransient<ProcessExpiredSubscriptionsJob>();
        services.AddTransient<ProcessRenewalRemindersJob>();
        services.AddTransient<ProcessInvoiceRetryJob>();
        services.AddTransient<ProcessUpgradeSlaExpiryJob>();
        services.AddTransient<ProcessScheduledDowngradeRemindersJob>();
        services.AddTransient<ProcessPriceChangeNotificationsJob>();
        services.AddTransient<ProcessPlanPriceChangesJob>();
        services.AddTransient<InvoiceDeliveryJob>();
        services.AddTransient<AdminBillingDigestJob>();
        services.AddTransient<RetryFailedPaymentWebhooksJob>();
        services.AddTransient<ProcessTrialRemindersJob>();
        services.AddTransient<ProcessDunningJob>();
        services.AddTransient<FlushUsageCountersJob>();

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

        var webhookOptions = app.ApplicationServices.GetRequiredService<IOptions<SubscriptionOptions>>().Value.PaymentWebhooks;
        recurringJobManager.AddOrUpdate<RetryFailedPaymentWebhooksJob>(
            "retry-failed-payment-webhooks",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            webhookOptions.RetryCron);

        var digestCron = app.ApplicationServices.GetRequiredService<IOptions<SubscriptionOptions>>()
            .Value.AdminNotifications.DigestCron;
        recurringJobManager.AddOrUpdate<AdminBillingDigestJob>(
            "admin-billing-digest",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            string.IsNullOrWhiteSpace(digestCron) ? "0 7 * * *" : digestCron);

        var billingGrowth = app.ApplicationServices.GetRequiredService<IOptions<SubscriptionOptions>>().Value;
        recurringJobManager.AddOrUpdate<ProcessTrialRemindersJob>(
            "process-trial-reminders",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            string.IsNullOrWhiteSpace(billingGrowth.Trials.ReminderCron) ? "30 9 * * *" : billingGrowth.Trials.ReminderCron);
        recurringJobManager.AddOrUpdate<ProcessDunningJob>(
            "process-dunning",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            string.IsNullOrWhiteSpace(billingGrowth.Dunning.Cron) ? "20 * * * *" : billingGrowth.Dunning.Cron);
        recurringJobManager.AddOrUpdate<FlushUsageCountersJob>(
            "flush-usage-counters",
            x => x.ExecuteAsync(JobCancellationToken.Null),
            string.IsNullOrWhiteSpace(billingGrowth.Metering.FlushCron) ? "5 * * * *" : billingGrowth.Metering.FlushCron);

        // Merged into the renewal orchestrator ("process-past-due"): cancellation → downgrade → past-due, in order.
        recurringJobManager.RemoveIfExists("process-scheduled-downgrades");
        recurringJobManager.RemoveIfExists("process-scheduled-cancellations");

        return app;
    }
}
