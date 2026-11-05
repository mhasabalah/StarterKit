using Hangfire.Common;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notification.Contracts.Realtime;
using StackExchange.Redis;
using RedisOptions = Shared.Core.Options.RedisOptions;

namespace Notification;

/// <summary>
/// The Notification module: delivers messages to people over every channel with one dispatch pipeline and one delivery
/// log (<c>notification.notification_log</c>):
/// <list type="bullet">
/// <item>email / WhatsApp - a fallback chain (preferences, fallback, retries, idempotency);</item>
/// <item>in-app - the user's inbox (<c>notification.inbox_notification</c>) + real-time SignalR events on
/// <see cref="NotificationHubContract.Path"/> (Redis backplane across replicas);</item>
/// <item>push - FCM to the user's session-bound devices (<c>notification.push_device</c>);</item>
/// <item>synchronous one-time code delivery (<see cref="IOtpDeliveryService"/>).</item>
/// </list>
/// Other modules use <c>Notification.Contracts</c> only (<see cref="INotificationSender"/>).
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.AddSingleton<NotificationMetrics>();

        AddEmailChannel(services);
        AddWhatsAppChannel(services);
        AddInAppChannel(services, configuration);
        AddPushChannel(services);

        // One-time code DELIVERY (synchronous, not through the dispatch job). Identity issues and verifies codes.
        services.AddTransient<IOtpDeliveryService, OtpDeliveryService>();

        services.AddCarterWithAssemblies(NotificationRoot.AssemblyName);
        services.AddMediator(NotificationRoot.AssemblyName);

        return services;
    }

    public static WebApplication UseNotificationModule(this WebApplication app)
    {
        app.UseMigration<NotificationDbContext>();

        GlobalJobFilters.Filters.Add(app.Services.GetRequiredService<NotificationFailedStateFilter>());

        MapRealtimeHub(app);

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
        var pushOptions = app.Services.GetRequiredService<IOptions<PushOptions>>().Value;
        logger.LogInformation(
            "Push provider: {Provider} (configured: {Configured}, credentials: {HasCredentials})",
            ResolvePushProvider(pushOptions), pushOptions.Provider, pushOptions.HasCredentials);

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

    /// <summary>Resolves <see cref="PushProvider.Auto"/>: FCM when credentials are configured, otherwise NoOp.</summary>
    public static PushProvider ResolvePushProvider(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Provider switch
        {
            PushProvider.Fcm => PushProvider.Fcm,
            PushProvider.NoOp => PushProvider.NoOp,
            _ => options.HasCredentials ? PushProvider.Fcm : PushProvider.NoOp
        };
    }

    /// <summary>Parses <see cref="RealtimeOptions.Transports"/>; an unknown name throws (also caught by options validation).</summary>
    public static HttpTransportType ParseTransports(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var result = HttpTransportType.None;
        foreach (var name in names)
        {
            result |= TryParseTransport(name, out var transport)
                ? transport
                : throw new InvalidOperationException($"Realtime:Transports contains an unknown transport '{name}'.");
        }

        return result;
    }

    private static bool TryParseTransport(string? name, out HttpTransportType transport) =>
        Enum.TryParse(name, ignoreCase: true, out transport)
        && transport is HttpTransportType.WebSockets or HttpTransportType.ServerSentEvents or HttpTransportType.LongPolling;

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
            .Validate(o => o.DefaultChannelOrder is null || !o.DefaultChannelOrder.Any(c => c.IsAdditive()),
                "Notification:DefaultChannelOrder must not contain InApp or Push (they are delivered alongside the chain).")
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

        // Push: the FCM service-account JSON is overlaid from the Docker secret (never stored in appsettings).
        services.AddOptions<PushOptions>()
            .Bind(configuration.GetSection(PushOptions.SectionName))
            .PostConfigure(o => o.CredentialsJson = configuration["fcm_service_account_json"] ?? o.CredentialsJson)
            .Validate(o => o.Provider != PushProvider.Fcm || o.HasCredentials,
                "Push:Provider is Fcm but no credentials are configured (secret fcm_service_account_json or Push:CredentialsFile).")
            .Validate(o => o.MaxAttempts is >= 1 and <= 10, "Push:MaxAttempts must be between 1 and 10.")
            .Validate(o => o.MaxDevicesPerUser is >= 1 and <= 100, "Push:MaxDevicesPerUser must be between 1 and 100.")
            .Validate(o => o.StaleDeviceDays > 0, "Push:StaleDeviceDays must be > 0.")
            .Validate(o => o.DefaultTimeToLive > TimeSpan.Zero && o.DefaultTimeToLive <= TimeSpan.FromDays(28),
                "Push:DefaultTimeToLive must be between 1 second and 28 days (the FCM maximum).")
            .ValidateOnStart();

        services.AddOptions<InboxOptions>()
            .Bind(configuration.GetSection(InboxOptions.SectionName))
            .Validate(o => o.RetentionDays > 0, "Inbox:RetentionDays must be > 0.")
            .ValidateOnStart();

        services.AddOptions<RealtimeOptions>()
            .Bind(configuration.GetSection(RealtimeOptions.SectionName))
            .Validate(o => o.TransportNames is { Length: > 0 } && o.TransportNames.All(t => TryParseTransport(t, out _)),
                "Realtime:Transports must list at least one of WebSockets, ServerSentEvents, LongPolling (and nothing else).")
            .Validate(o => o.KeepAliveInterval > TimeSpan.Zero && o.ClientTimeoutInterval > o.KeepAliveInterval,
                "Realtime:ClientTimeoutInterval must be greater than Realtime:KeepAliveInterval (> 0).")
            .ValidateOnStart();
    }

    private static void AddInAppChannel(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IInboxNotificationRepository, InboxNotificationRepository>();
        services.AddTransient<InAppDeliveryJob>();

        var realtime = configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>() ?? new RealtimeOptions();
        if (!realtime.Enabled)
        {
            services.AddSingleton<IRealtimeNotifier, NoOpRealtimeNotifier>();
            return;
        }

        var signalR = services.AddSignalR(o =>
        {
            o.KeepAliveInterval = realtime.KeepAliveInterval;
            o.ClientTimeoutInterval = realtime.ClientTimeoutInterval;
            // Server -> client only: clients send nothing but the handshake and pings.
            o.MaximumReceiveMessageSize = 4 * 1024;
            o.EnableDetailedErrors = false;
        });
        services.AddSingleton<IUserIdProvider, NotificationHubUserIdProvider>();
        services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

        // Scale-out: with Redis on, every replica relays hub sends through Redis pub/sub, so a send from the replica
        // that runs the Hangfire job reaches a socket held by another replica. A DEDICATED connection: the backplane
        // owns (and disposes) its multiplexer, and pub/sub traffic stays off the cache connection.
        var redis = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (realtime.UseRedisBackplane && redis.Enabled)
        {
            // Resolved lazily (first hub use), like every other options value; AddRedisCache already fails fast on a
            // missing production secret.
            signalR.AddStackExchangeRedis(o =>
            {
                o.Configuration = RedisConnectionConfiguration.Resolve(configuration, redis).Clone();
                o.Configuration.ChannelPrefix = RedisChannel.Literal(redis.InstanceName.TrimEnd(':') + ":signalr");
            });
        }
    }

    private static void AddPushChannel(IServiceCollection services)
    {
        services.AddTransient<IPushDeviceRepository, PushDeviceRepository>();
        services.AddTransient<PushDeliveryJob>();
        services.AddSingleton<FcmPushProvider>();
        services.AddSingleton<NoOpPushProvider>();
        services.AddSingleton<IPushProvider>(sp =>
            ResolvePushProvider(sp.GetRequiredService<IOptions<PushOptions>>().Value) == PushProvider.Fcm
                ? sp.GetRequiredService<FcmPushProvider>()
                : sp.GetRequiredService<NoOpPushProvider>());
    }

    private static void MapRealtimeHub(WebApplication app)
    {
        var realtime = app.Services.GetRequiredService<IOptions<RealtimeOptions>>().Value;
        if (!realtime.Enabled)
            return;

        app.MapHub<NotificationHub>(NotificationHubContract.Path, o =>
            {
                o.Transports = ParseTransports(realtime.TransportNames);
                // Drop the socket when its access token expires: a revoked session or password change cannot keep a
                // connection alive beyond one access-token lifetime (clients reconnect with a fresh token).
                o.CloseOnAuthenticationExpiration = true;
                o.ApplicationMaxBufferSize = 4 * 1024;
            })
            .RequireAuthorization()
            .DisallowApiKey();
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
