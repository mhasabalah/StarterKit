using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Contracts.FeatureFlags;
using Shared.Core.Exceptions;

namespace Shared.Core.FeatureFlags;

/// <summary>
/// One stored flag with its overrides, as cached and evaluated by <see cref="FeatureFlagEvaluator"/>.
/// <see cref="RolloutPercent"/> 0 = no rollout (use <see cref="Enabled"/>), 1..99 = that share of tenants, 100 = everyone.
/// </summary>
public sealed record FeatureFlagDefinition(
    string Key,
    bool Enabled,
    int RolloutPercent,
    bool KillSwitch,
    Dictionary<Guid, bool> TenantOverrides,
    Dictionary<Guid, bool> UserOverrides);

/// <summary>The flag evaluation rules, pure and deterministic (no I/O), shared by every implementation.</summary>
public static class FeatureFlagEvaluator
{
    /// <summary>
    /// kill switch (off) -> user override -> tenant override -> percentage rollout -> flag default.
    /// <paramref name="flag"/> <c>null</c> (not stored) -> <paramref name="configuredDefault"/>.
    /// </summary>
    public static bool Evaluate(FeatureFlagDefinition? flag, Guid? tenantId, Guid? userId, bool configuredDefault)
    {
        if (flag is null) return configuredDefault;
        if (flag.KillSwitch) return false;

        if (userId is { } user && flag.UserOverrides.TryGetValue(user, out var userValue)) return userValue;
        if (tenantId is { } tenant && flag.TenantOverrides.TryGetValue(tenant, out var tenantValue)) return tenantValue;

        if (flag.RolloutPercent > 0)
            return flag.RolloutPercent >= 100 || Bucket(flag.Key, tenantId) < flag.RolloutPercent;

        return flag.Enabled;
    }

    /// <summary>
    /// Stable bucket 0..99 of (flag key, tenant): SHA-256, first four bytes big-endian, modulo 100. Never
    /// <c>string.GetHashCode</c> (randomized per process), so every replica and every restart agrees, and raising the
    /// percentage only ever ADDS tenants. Each flag buckets tenants independently because the key is part of the input.
    /// </summary>
    public static int Bucket(string key, Guid? tenantId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{(tenantId ?? Guid.Empty):N}"), hash);
        var value = (uint)hash[0] << 24 | (uint)hash[1] << 16 | (uint)hash[2] << 8 | hash[3];
        return (int)(value % 100);
    }
}

/// <summary>Flag defaults from configuration: <c>FeatureFlags:Defaults:&lt;key&gt;: true|false</c>.</summary>
public sealed class FeatureFlagDefaults(IConfiguration configuration)
{
    public const string Section = "FeatureFlags:Defaults";

    public bool Get(string key) => configuration.GetSection(Section).GetValue<bool?>(key) ?? false;

    public IReadOnlyDictionary<string, bool> All() => configuration.GetSection(Section).GetChildren()
        .Where(c => bool.TryParse(c.Value, out _))
        .ToDictionary(c => c.Key, c => bool.Parse(c.Value!), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Default <see cref="IFeatureFlags"/> when no module stores flags: only <c>FeatureFlags:Defaults</c>.</summary>
public sealed class ConfigFeatureFlags(FeatureFlagDefaults defaults) : IFeatureFlags
{
    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(defaults.Get(key));

    public Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(defaults.All());
}

public static class FeatureFlagExtensions
{
    /// <summary>
    /// Registers the configuration-backed default. A module that stores flags (Tenancy) registers its own scoped
    /// <see cref="IFeatureFlags"/> (AddScoped replaces this one regardless of call order).
    /// </summary>
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services)
    {
        services.TryAddSingleton<FeatureFlagDefaults>();
        services.TryAddScoped<IFeatureFlags, ConfigFeatureFlags>();
        return services;
    }

    /// <summary>
    /// Endpoint exists only while flag <paramref name="key"/> is on for the caller; otherwise 404, exactly as if the
    /// route did not exist (a dark-launched feature must not be discoverable). Does not replace authentication or
    /// permission checks, which run before it.
    /// </summary>
    public static TBuilder RequireFlag<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>();
            if (!await flags.IsEnabledAsync(key, context.HttpContext.RequestAborted))
                throw new NotFoundException("The requested resource was not found.");

            return await next(context);
        });
}
