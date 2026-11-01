using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Shared.Core.Extensions;

/// <summary>
/// Generic, configuration-driven rate limiting setup.
/// Define all policies in appsettings under RateLimiting:Policies[].
/// </summary>
public static class RateLimitingExtensions
{
    private const string SectionName = "RateLimiting";

    public static IServiceCollection AddConfigurableRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var policies = configuration
                           .GetSection($"{SectionName}:Policies")
                           .Get<List<RateLimitPolicyOptions>>()
                       ?? DefaultPolicies;

        services.AddRateLimiter(options =>
        {
            foreach (var policy in policies)
            {
                options.AddFixedWindowLimiter(policy.Name, config =>
                {
                    config.PermitLimit = policy.PermitLimit;
                    config.Window = TimeSpan.FromSeconds(policy.WindowSeconds);
                    config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    config.QueueLimit = policy.QueueLimit;
                });
            }

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    /// <summary>
    /// Sensible defaults used when RateLimiting:Policies is not configured.
    /// Override in appsettings.json for environment-specific tuning.
    /// </summary>
    private static readonly List<RateLimitPolicyOptions> DefaultPolicies =
    [
        new("upload", 5, 60, 0),
        new("create", 10, 60, 0),
        new("search", 60, 60, 0)
    ];
}

public record RateLimitPolicyOptions(
    string Name,
    int PermitLimit,
    int WindowSeconds,
    int QueueLimit = 0);
