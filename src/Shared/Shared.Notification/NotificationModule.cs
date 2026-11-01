using Hangfire.Common;
using Shared.Email.Contracts;
using Shared.Email.Services;
using Shared.Notification.Channels.Email;
using Shared.Notification.Channels.WhatsApp;

namespace Shared.Notification;

public static class NotificationModule
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection("Notification"));
        services.Configure<VerificationOptions>(
            configuration.GetSection("Notification:Verification"));

        var whatsAppOptions = new WhatsAppOptions();
        configuration.GetSection("WhatsApp").Bind(whatsAppOptions);
        whatsAppOptions.AccessToken = configuration["whatsapp_access_token"] ?? whatsAppOptions.AccessToken;
        whatsAppOptions.PhoneNumberId = configuration["whatsapp_phone_number_id"] ?? whatsAppOptions.PhoneNumberId;
        services.AddSingleton<IOptions<WhatsAppOptions>>(Microsoft.Extensions.Options.Options.Create(whatsAppOptions));

        // Validates all WhatsAppTemplate enum values have config entries at startup.
        services.AddSingleton(new WhatsAppTemplateRegistry(whatsAppOptions));

        services.AddCustomDbContext<NotificationDbContext>(configuration, "starterkit");

        services.AddTransient<NotificationLogRepository>();
        services.AddTransient<INotificationJobEnqueuer, NotificationJobEnqueuer>();
        services.AddTransient<INotificationSender, NotificationSender>();

        services.AddTransient<NotificationDispatchJob>();
        services.AddTransient<NotificationDailyPurgeJob>();
        services.AddTransient<IVerificationService, VerificationService>();
        services.AddSingleton<NotificationFailedStateFilter>();

        services.AddTransient<INotificationChannelAdapter, EmailChannelAdapter>();
        services.AddTransient<INotificationChannelAdapter, WhatsAppChannelAdapter>();

        services.AddHttpClient<WhatsAppApiClient>(client =>
            {
                client.BaseAddress = new Uri($"https://graph.facebook.com/{whatsAppOptions.ApiVersion}/");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", whatsAppOptions.AccessToken);
            })
            .AddStandardResilienceHandler();

        services.AddCarterWithAssemblies(NotificationRoot.AssemblyName);
        services.AddMediatR(NotificationRoot.AssemblyName);

        return services;
    }

    public static WebApplication UseNotificationInfrastructure(this WebApplication app)
    {
        app.UseMigration<NotificationDbContext>();

        GlobalJobFilters.Filters.Add(
            app.Services.GetRequiredService<NotificationFailedStateFilter>());

        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate(
            "notification-daily-purge",
            Job.FromExpression<NotificationDailyPurgeJob>(
                job => job.ExecuteAsync(CancellationToken.None)),
            Cron.Daily(3));

        return app;
    }
}
