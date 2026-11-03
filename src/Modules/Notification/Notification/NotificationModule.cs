using Hangfire.Common;

namespace Notification;

/// <summary>
/// The Notification module: delivers messages to people over every channel (email, WhatsApp) with one dispatch
/// pipeline (preferences, fallback, retries, idempotency), one delivery log (<c>notification.notification_log</c>)
/// and synchronous one-time code delivery (<see cref="IOtpDeliveryService"/>). Other modules use <c>Notification.Contracts</c> only (<see cref="INotificationSender"/>).
/// </summary>
public static class NotificationModule
{
    public const string ConnectionStringName = "starterkit";

    public static IServiceCollection AddNotificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        AddOptions(services, configuration);

        services.AddCustomDbContext<NotificationDbContext>(configuration, ConnectionStringName);

        // Dispatch pipeline.
        services.AddTransient<NotificationLogRepository>();
        services.AddTransient<INotificationLogRepository>(sp => sp.GetRequiredService<NotificationLogRepository>());
        services.AddTransient<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddTransient<INotificationJobEnqueuer, NotificationJobEnqueuer>();
        services.AddTransient<INotificationSender, NotificationSender>();
        services.AddSingleton<INotificationLinkResolver, NotificationLinkResolver>();
        services.AddTransient<NotificationDispatchJob>();
        services.AddTransient<NotificationDailyPurgeJob>();
        services.AddTransient<NotificationDailyFailureSummaryJob>();
        services.AddSingleton<NotificationFailedStateFilter>();

        AddEmailChannel(services);
        AddWhatsAppChannel(services);

        // One-time code DELIVERY (synchronous, not through the dispatch job). Identity issues and verifies codes.
        services.AddTransient<IOtpDeliveryService, OtpDeliveryService>();

        services.AddCarterWithAssemblies(NotificationRoot.AssemblyName);
        services.AddMediatR(NotificationRoot.AssemblyName);

        return services;
    }

    public static WebApplication UseNotificationModule(this WebApplication app)
    {
        app.UseMigration<NotificationDbContext>();

        GlobalJobFilters.Filters.Add(app.Services.GetRequiredService<NotificationFailedStateFilter>());

        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate(
            "notification-daily-purge",
            Job.FromExpression<NotificationDailyPurgeJob>(job => job.ExecuteAsync(CancellationToken.None)),
            Cron.Daily(3));
        recurringJobs.AddOrUpdate(
            "notification-daily-failure-summary",
            Job.FromExpression<NotificationDailyFailureSummaryJob>(job => job.ExecuteAsync(CancellationToken.None)),
            Cron.Daily(7));

        // Jobs of the former Shared.Email assembly (types no longer exist): drop them from existing Hangfire storage.
        recurringJobs.RemoveIfExists("email-daily-failure-summary");
        recurringJobs.RemoveIfExists("email-log-daily-purge");

        var emailOptions = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Notification");
        logger.LogInformation("Email provider: {Provider}", ResolveEmailProvider(emailOptions, app.Environment));

        // Smoke-render every email template in every culture with a populated sample model.
        // Development: throw (fail loudly). Elsewhere: log a warning per broken template.
        SmokeRenderAllTemplates(
            app.Services.GetRequiredService<IEmailTemplateRenderer>(),
            app.Services.GetRequiredService<EmailTemplateSamples>(),
            logger,
            throwOnFailure: app.Environment.IsDevelopment());

        return app;
    }

    /// <summary>Resolves <see cref="EmailProvider.Auto"/> to a concrete provider.</summary>
    public static EmailProvider ResolveEmailProvider(EmailOptions options, IHostEnvironment environment)
        => options.Provider switch
        {
            EmailProvider.Smtp => EmailProvider.Smtp,
            EmailProvider.NoOp => EmailProvider.NoOp,
            _ => environment.IsDevelopment() && !options.IsSmtpHostConfigured
                ? EmailProvider.NoOp
                : EmailProvider.Smtp
        };

    /// <summary>Renders every template × culture; returns the failures (template, culture, exception).</summary>
    public static IReadOnlyList<(EmailTemplate Template, NotificationCulture Culture, Exception Error)> SmokeRenderAllTemplates(
        IEmailTemplateRenderer renderer,
        EmailTemplateSamples samples,
        ILogger logger,
        bool throwOnFailure)
    {
        var failures = new List<(EmailTemplate, NotificationCulture, Exception)>();

        foreach (var template in Enum.GetValues<EmailTemplate>())
        {
            var templateName = FluidTemplateRenderer.GetTemplateFileName(template);
            foreach (var culture in Enum.GetValues<NotificationCulture>())
            {
                try
                {
                    renderer.RenderAsync(templateName, samples.Build(template), culture).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    failures.Add((template, culture, ex));
                    logger.LogWarning(
                        ex,
                        "Email template smoke-render failed. Template: {Template} Culture: {Culture}",
                        templateName, culture);
                }
            }
        }

        if (failures.Count > 0 && throwOnFailure)
            throw new AggregateException(
                $"{failures.Count} email template render(s) failed at startup: " +
                string.Join(", ", failures.Select(f => $"{f.Item1}/{f.Item2}")),
                failures.Select(f => f.Item3));

        return failures;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection("Notification"))
            .Validate(o => o.DefaultChannelOrder is { Length: > 0 }, "Notification:DefaultChannelOrder must not be empty.")
            .Validate(o => o.FallbackAfterAttempts > 0, "Notification:FallbackAfterAttempts must be > 0.")
            .ValidateOnStart();

        // SMTP config; credentials overlaid from Docker secrets (never stored in appsettings).
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .PostConfigure(o =>
            {
                o.SmtpUsername = configuration["smtp_username"] ?? o.SmtpUsername;
                o.SmtpPassword = configuration["smtp_password"] ?? o.SmtpPassword;
            });

        // WhatsApp: credentials overlaid from Docker secrets; every template must have a MetaTemplateName (ValidateOnStart).
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection("WhatsApp"))
            .PostConfigure(o =>
            {
                o.AccessToken = configuration["whatsapp_access_token"] ?? o.AccessToken;
                o.PhoneNumberId = configuration["whatsapp_phone_number_id"] ?? o.PhoneNumberId;
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();
        services.AddSingleton(sp => new WhatsAppTemplateRegistry(sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value));
    }

    private static void AddEmailChannel(IServiceCollection services)
    {
        services.AddSingleton<IEmailTemplateRenderer, FluidTemplateRenderer>();

        // One IEmailTemplate (subject + model type) per template, discovered in this assembly (they are internal).
        services.Scan(scan => scan
            .FromAssemblyOf<EmailTemplateRegistry>()
            .AddClasses(c => c.AssignableTo<IEmailTemplate>(), publicOnly: false)
            .AsImplementedInterfaces()
            .WithSingletonLifetime());
        services.AddSingleton<EmailTemplateRegistry>();
        services.AddSingleton<EmailTemplateSamples>();

        // Email:Provider = Smtp | NoOp | Auto (default). Auto keeps a dev box from mailing real
        // users: NoOp in Development unless a real SMTP host is configured (e.g. Mailhog).
        services.AddTransient<MailKitEmailService>();
        services.AddTransient<NoOpEmailService>();
        services.AddTransient<IEmailService>(sp =>
            ResolveEmailProvider(
                sp.GetRequiredService<IOptions<EmailOptions>>().Value,
                sp.GetRequiredService<IHostEnvironment>()) == EmailProvider.NoOp
                ? sp.GetRequiredService<NoOpEmailService>()
                : sp.GetRequiredService<MailKitEmailService>());

        services.AddTransient<INotificationChannelAdapter, EmailChannelAdapter>();
    }

    private static void AddWhatsAppChannel(IServiceCollection services)
    {
        services.AddTransient<INotificationChannelAdapter, WhatsAppChannelAdapter>();

        services.AddHttpClient<WhatsAppApiClient>((sp, client) =>
            {
                var wa = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
                client.BaseAddress = new Uri($"https://graph.facebook.com/{wa.ApiVersion}/");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", wa.AccessToken);
            })
            // Timeouts + circuit breaker only. Retries are owned by ONE layer - the Hangfire dispatch job,
            // which knows permanent (4xx) from transient failures and never re-sends a delivered message.
            .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => ValueTask.FromResult(false));
    }
}
