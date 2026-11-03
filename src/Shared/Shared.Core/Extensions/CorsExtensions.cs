using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Core.Web;

namespace Shared.Core.Extensions;

public static class CorsExtensions
{
    /// <summary>
    /// Registers the CORS policy from the <c>Cors</c> section.
    /// An empty <c>Cors:AllowedOrigins</c> is only tolerated in Development (any origin, no credentials);
    /// in every other environment startup fails, so a missing setting can never silently open the API to every origin.
    /// An explicit <c>"*"</c> is still honoured (without credentials) as a deliberate choice.
    /// </summary>
    public static IServiceCollection AddConfigurableCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var settings = configuration
            .GetSection(CorsSettings.SectionName)
            .Get<CorsSettings>() ?? new CorsSettings();

        var origins = settings.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToArray();

        if (origins.Length == 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{CorsSettings.SectionName}:AllowedOrigins is empty in the '{environment.EnvironmentName}' environment. " +
                "Configure the allowed front-end origins (e.g. Cors__AllowedOrigins__0=https://app.example.com), " +
                "or set it to [\"*\"] to deliberately allow any origin (credentials are then disabled).");
        }

        var allowAny = origins.Length == 0 || origins.Contains("*");

        services.AddCors(options =>
        {
            options.AddPolicy(CorsSettings.PolicyName, policy =>
            {
                ApplyOrigins(policy, origins, allowAny, settings.AllowCredentials);

                if (settings.AllowedMethods.Contains("*"))
                    policy.AllowAnyMethod();
                else
                    policy.WithMethods(settings.AllowedMethods);

                if (settings.AllowedHeaders.Contains("*"))
                    policy.AllowAnyHeader();
                else
                    policy.WithHeaders(settings.AllowedHeaders);

                policy.WithExposedHeaders(CorrelationExtensions.HeaderName, "Retry-After");
            });

            // Also set as the default policy — ensures CORS works even if
            // the named policy lookup fails for any reason.
            options.AddDefaultPolicy(policy =>
            {
                ApplyOrigins(policy, origins, allowAny, settings.AllowCredentials);
                policy.AllowAnyMethod();
                policy.AllowAnyHeader();
            });
        });

        return services;
    }

    private static void ApplyOrigins(CorsPolicyBuilder policy, string[] origins, bool allowAny, bool allowCredentials)
    {
        if (allowAny)
        {
            // Wildcard origin cannot be combined with AllowCredentials
            policy.AllowAnyOrigin();
            return;
        }

        policy.WithOrigins(origins);
        if (allowCredentials)
            policy.AllowCredentials();
    }

    public static WebApplication UseConfigurableCors(this WebApplication app)
    {
        app.UseCors(CorsSettings.PolicyName);
        return app;
    }
}
