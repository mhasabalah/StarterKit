using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Exceptions;

namespace Shared.Core.Entitlements;

/// <summary>
/// Counts usage of a metered feature for the current subscriber (the tenant in multi-tenant mode, the signed-in user
/// otherwise). The billing module provides the real implementation; the default does nothing.
/// </summary>
public interface IUsageMeter
{
    /// <summary>Adds <paramref name="quantity"/> to the feature's counter for the current period. Never throws on store failure.</summary>
    Task RecordAsync(string key, long quantity = 1, CancellationToken cancellationToken = default);

    /// <summary>Usage of the feature in the current period (0 when nothing is metered).</summary>
    Task<long> GetUsageAsync(string key, CancellationToken cancellationToken = default);
}

/// <param name="Allowed">False = the quota would be exceeded; nothing was consumed.</param>
/// <param name="Used">Usage after the call (unchanged when denied).</param>
/// <param name="Limit">The plan limit; null = unlimited.</param>
public readonly record struct QuotaResult(bool Allowed, long Used, long? Limit);

/// <summary>Atomic "check the plan limit and count the usage" for a metered feature.</summary>
public interface IQuotaService
{
    /// <summary>
    /// Consumes <paramref name="quantity"/> when the plan limit allows it; otherwise consumes nothing and returns
    /// <c>Allowed = false</c>. Fails OPEN (allowed) when the counter store is unavailable.
    /// </summary>
    Task<QuotaResult> TryConsumeAsync(string key, long quantity = 1, CancellationToken cancellationToken = default);
}

/// <summary>Default when no billing module is installed: nothing is counted and everything is allowed.</summary>
public sealed class NoopUsageMeter : IUsageMeter
{
    public Task RecordAsync(string key, long quantity = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<long> GetUsageAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(0L);
}

/// <summary>Default when no billing module is installed: no limits.</summary>
public sealed class PermissiveQuotaService : IQuotaService
{
    public Task<QuotaResult> TryConsumeAsync(string key, long quantity = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuotaResult(true, 0, null));
}

/// <summary>The plan's quota for a feature is used up (429, <c>errorCode = quota_exceeded</c>).</summary>
public sealed class QuotaExceededException : CustomException
{
    public const string ErrorCodeValue = "quota_exceeded";

    public QuotaExceededException(string key, long used, long? limit)
        : base($"Your plan's quota for '{key}' is used up ({used}/{limit}).", HttpStatusCode.TooManyRequests)
    {
        ErrorCode = ErrorCodeValue;
        Key = key;
        Used = used;
        Limit = limit;
    }

    public string Key { get; }
    public long Used { get; }
    public long? Limit { get; }
}

public static class QuotaExtensions
{
    /// <summary>
    /// Registers the permissive defaults (nothing counted, no limits). A billing module registers scoped
    /// implementations of both ports (AddScoped replaces these regardless of call order).
    /// </summary>
    public static IServiceCollection AddQuotaDefaults(this IServiceCollection services)
    {
        services.TryAddScoped<IUsageMeter, NoopUsageMeter>();
        services.TryAddScoped<IQuotaService, PermissiveQuotaService>();
        return services;
    }

    /// <summary>
    /// Consumes <paramref name="quantity"/> of the metered feature <paramref name="key"/> BEFORE the endpoint runs and
    /// answers 429 with <c>errorCode = quota_exceeded</c> when the plan limit would be exceeded. Usage is counted per
    /// attempt (a later failure of the endpoint does not give it back). Fails open when the counter store is down.
    /// Does not replace authentication or permission checks.
    /// </summary>
    public static TBuilder RequireQuota<TBuilder>(this TBuilder builder, string key, long quantity = 1)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return builder.AddEndpointFilter(async (context, next) =>
        {
            var quotas = context.HttpContext.RequestServices.GetRequiredService<IQuotaService>();
            var result = await quotas.TryConsumeAsync(key, quantity, context.HttpContext.RequestAborted);
            if (!result.Allowed)
                throw new QuotaExceededException(key, result.Used, result.Limit);

            return await next(context);
        });
    }
}
