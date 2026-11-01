using Hangfire.Common;
using Shared.Email.Options;
using Shared.Email.Services;

namespace Shared.Email;

public static class EmailModule
{
    public static IServiceCollection AddEmailInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind SMTP config; overlay credentials from Docker secrets (never stored in appsettings)
        var options = new EmailOptions();
        configuration.GetSection("Email").Bind(options);
        options.SmtpUsername = configuration["smtp_username"] ?? options.SmtpUsername;
        options.SmtpPassword = configuration["smtp_password"] ?? options.SmtpPassword;
        services.AddSingleton<IOptions<EmailOptions>>(Microsoft.Extensions.Options.Options.Create(options));

        services.AddMediatR(EmailRoot.AssemblyName);

        services.AddCarterWithAssemblies(EmailRoot.AssemblyName);

        services.AddCustomDbContext<EmailDbContext>(configuration, "starterkit");

        services.AddSingleton<IEmailTemplateRenderer, FluidTemplateRenderer>();

        // Auto-discover all IEmailTemplate implementations (one per model file).
        // Adding a new template = add a model that implements IEmailTemplate. Nothing else changes.
        services.Scan(scan => scan
            .FromAssemblyOf<EmailOptions>()
            .AddClasses(c => c.AssignableTo<IEmailTemplate>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());
        services.AddSingleton<EmailTemplateRegistry>();

        //if (string.Equals(
        //        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        //        "Production",
        //        StringComparison.OrdinalIgnoreCase))
        //{
            services.AddTransient<IEmailService, MailKitEmailService>();
        //}
        //else
        //{
        //    services.AddTransient<IEmailService, NoOpEmailService>();
        //}

        services.AddTransient<IEmailJobEnqueuer, EmailJobEnqueuer>();
        services.AddTransient<EmailJob>();
        services.AddTransient<EmailLogRepository>();
        services.AddTransient<EmailDailySummaryJob>();

        services.AddSingleton<EmailFailedStateFilter>();

        return services;
    }

    public static WebApplication UseEmailInfrastructure(this WebApplication app)
    {
        app.UseMigration<EmailDbContext>();

        // Register the dead-letter alerting filter via the DI-resolved instance so it
        // receives a proper ILogger<EmailFailedStateFilter> rather than using the
        // static Log.Fatal. Must happen here (after app is built) not in AddEmailInfrastructure.
        GlobalJobFilters.Filters.Add(app.Services.GetRequiredService<EmailFailedStateFilter>());

        // Register the daily failure summary as a recurring Hangfire job.
        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate(
            "email-daily-failure-summary",
            Job.FromExpression<EmailDailySummaryJob>(job => job.ExecuteAsync(CancellationToken.None)),
            Cron.Daily(7));

        // CQ4: Smoke-render every template in both cultures at startup.
        // Catches broken Liquid syntax and missing variables before the first real send.
        SmokeRenderAllTemplates(app);

        return app;
    }

    private static void SmokeRenderAllTemplates(WebApplication app)
    {
        var renderer = app.Services.GetRequiredService<IEmailTemplateRenderer>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Shared.Email.SmokeRender");
        var emptyModel = new Dictionary<string, object>();

        foreach (var template in Enum.GetValues<EmailTemplate>())
        {
            var templateName = FluidTemplateRenderer.GetTemplateFileName(template);
            foreach (var culture in Enum.GetValues<EmailCulture>())
            {
                try
                {
                    renderer.RenderAsync(templateName, emptyModel, culture).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Email template smoke-render failed. Template: {Template} Culture: {Culture}",
                        templateName, culture);
                }
            }
        }
    }
}
