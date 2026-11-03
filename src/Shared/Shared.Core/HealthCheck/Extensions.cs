using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Shared.Core.Data;
using Shared.Core.Extensions;

namespace Shared.Core.HealthCheck;

public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    /// <summary>Health check name of the application database (tag <see cref="HealthCheckTags.Ready"/>).</summary>
    public const string DatabaseHealthCheckName = "postgres";

    /// <summary>
    /// Registers the liveness check and the database readiness check. The database connection string is
    /// <c>ConnectionStrings:{HealthOptions:DatabaseConnectionStringName}</c> (Aspire / modules), falling back to
    /// <c>PostgresOptions:ConnectionString</c> - the same resolution the module DbContexts use.
    /// Redis registers its own readiness check in <see cref="RedisExtensions.AddRedisCache"/>.
    /// </summary>
    public static IServiceCollection AddCustomHealthCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthOptions = configuration.GetSection(nameof(HealthOptions)).Get<HealthOptions>() ?? new HealthOptions();
        if (!healthOptions.Enabled)
        {
            return services;
        }

        var healthChecks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [HealthCheckTags.Live]);

        var databaseConnectionString =
            configuration.GetConnectionString(healthOptions.DatabaseConnectionStringName) is { Length: > 0 } named
                ? named
                : configuration[$"{nameof(PostgresOptions)}:{nameof(PostgresOptions.ConnectionString)}"];

        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            healthChecks.AddNpgSql(databaseConnectionString, name: DatabaseHealthCheckName, tags: [HealthCheckTags.Ready]);
        }

        services.AddHealthChecksUI(setup => setup.SetEvaluationTimeInSeconds(60)).AddInMemoryStorage();

        return services;
    }

    public static WebApplication UseCustomHealthCheck(this WebApplication app)
    {
        var healthOptions = app.Configuration.GetOptions<HealthOptions>(nameof(HealthOptions));

        // Readiness: every check (process + dependencies). Used by Aspire, Docker and the deploy health gate.
        app.MapHealthChecks(HealthEndpointPath);

        // Liveness: process only - a dependency outage must not get a healthy process restarted.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(HealthCheckTags.Live)
        });

        if (healthOptions.Enabled && !app.Environment.IsProduction())
        {
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");
        }

        return app;
    }
}
