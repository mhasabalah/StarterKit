using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Shared.Core.Data;
using Shared.Core.Extensions;
using Shared.Core.Web;

namespace Shared.Core.HealthCheck;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services)
    {
        var healthOptions = services.GetOptions<HealthOptions>(nameof(HealthOptions));

        if (healthOptions.Enabled)
        {
            var appOptions = services.GetOptions<AppOptions>(nameof(AppOptions));
            var postgresOptions = services.GetOptions<PostgresOptions>(nameof(PostgresOptions));

            var healthChecksBuilder = services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

            if (!string.IsNullOrEmpty(postgresOptions.ConnectionString))
            {
                healthChecksBuilder.AddNpgSql(postgresOptions.ConnectionString);
            }

            services.AddHealthChecksUI(setup =>
            {
                setup.SetEvaluationTimeInSeconds(60); // time in seconds between check
                // Note: Self-monitoring not configured in Aspire environment due to service discovery limitations
                // Health checks are still available at /health endpoint for external monitoring
            }).AddInMemoryStorage();
        }

        return services;
    }

    public static WebApplication UseCustomHealthCheck(this WebApplication app)
    {
        var healthOptions = app.Configuration.GetOptions<HealthOptions>(nameof(HealthOptions));

        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        if (healthOptions.Enabled && !app.Environment.IsProduction())
        {
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");
        }

        return app;
    }
}
