using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Web;

namespace Shared.Core.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddConfigurableCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection(CorsSettings.SectionName)
            .Get<CorsSettings>() ?? new CorsSettings();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsSettings.PolicyName, policy =>
            {
                if (settings.AllowedOrigins.Length == 0)
                {
                    // No origins configured — allow any origin as safe fallback
                    // so CORS middleware still intercepts preflight requests.
                    policy.AllowAnyOrigin();
                }
                else if (settings.AllowedOrigins.Contains("*"))
                {
                    // Wildcard origin cannot be combined with AllowCredentials
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(settings.AllowedOrigins);

                    if (settings.AllowCredentials)
                        policy.AllowCredentials();
                }

                if (settings.AllowedMethods.Contains("*"))
                    policy.AllowAnyMethod();
                else
                    policy.WithMethods(settings.AllowedMethods);

                if (settings.AllowedHeaders.Contains("*"))
                    policy.AllowAnyHeader();
                else
                    policy.WithHeaders(settings.AllowedHeaders);
            });

            // Also set as the default policy — ensures CORS works even if
            // the named policy lookup fails for any reason.
            options.AddDefaultPolicy(policy =>
            {
                if (settings.AllowedOrigins.Length == 0 || settings.AllowedOrigins.Contains("*"))
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(settings.AllowedOrigins);
                    if (settings.AllowCredentials)
                        policy.AllowCredentials();
                }

                policy.AllowAnyMethod();
                policy.AllowAnyHeader();
            });
        });

        return services;
    }

    public static WebApplication UseConfigurableCors(this WebApplication app)
    {
        app.UseCors(CorsSettings.PolicyName);
        return app;
    }
}
