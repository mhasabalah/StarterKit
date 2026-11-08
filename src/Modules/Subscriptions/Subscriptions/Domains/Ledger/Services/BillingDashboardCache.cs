using System.Globalization;
using System.Text.Json;
using Subscriptions.Options;

namespace Subscriptions.Domains.Ledger.Services;

/// <summary>
/// Caches <see cref="BillingDashboardDto"/> per date range in <see cref="IDistributedCache"/>.
/// <para>
/// The ranges are open-ended, so one invalidation must drop all of them. Every key embeds a generation token stored
/// under <see cref="BillingDashboardCache.GenerationKey"/>. <see cref="InvalidateAsync"/> writes a new token, so all
/// earlier entries stop being read and expire through their TTL (<c>Subscriptions:BillingDashboardCacheMinutes</c>,
/// the safety net). The token lives in the shared cache (Redis), so every replica sees an invalidation at once.
/// </para>
/// Failures are logged and swallowed: the cache must never fail a request or the money event that invalidates it.
/// </summary>
public interface IBillingDashboardCache
{
    Task<BillingDashboardDto?> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task SetAsync(DateTimeOffset from, DateTimeOffset to, BillingDashboardDto dashboard, CancellationToken cancellationToken = default);

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public sealed class BillingDashboardCache(
    IDistributedCache cache,
    IOptions<SubscriptionOptions> options,
    ILogger<BillingDashboardCache> logger) : IBillingDashboardCache
{
    public const string GenerationKey = "subscriptions:billing-dashboard:generation";

    private const string InitialGeneration = "0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string KeyFor(string generation, DateTimeOffset from, DateTimeOffset to) =>
        string.Create(CultureInfo.InvariantCulture,
            $"subscriptions:billing-dashboard:v2:{generation}:{from.UtcTicks}:{to.UtcTicks}");

    private TimeSpan? Ttl
    {
        get
        {
            var minutes = Math.Max(0, options.Value.BillingDashboardCacheMinutes);
            return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
        }
    }

    public async Task<BillingDashboardDto?> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (Ttl is null)
            return null;

        try
        {
            var generation = await CurrentGenerationAsync(cancellationToken);
            var json = await cache.GetStringAsync(KeyFor(generation, from, to), cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<BillingDashboardDto>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the billing dashboard from cache failed; computing it from the database");
            return null;
        }
    }

    public async Task SetAsync(DateTimeOffset from, DateTimeOffset to, BillingDashboardDto dashboard, CancellationToken cancellationToken = default)
    {
        if (Ttl is not { } ttl)
            return;

        try
        {
            var generation = await CurrentGenerationAsync(cancellationToken);
            await cache.SetStringAsync(
                KeyFor(generation, from, to),
                JsonSerializer.Serialize(dashboard, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Caching the billing dashboard failed");
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // No expiry: the token must outlive every entry written under the previous one.
            await cache.SetStringAsync(GenerationKey, Guid.NewGuid().ToString("N"), new DistributedCacheEntryOptions(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Invalidating the billing dashboard cache failed; it refreshes when its TTL ends");
        }
    }

    private async Task<string> CurrentGenerationAsync(CancellationToken cancellationToken) =>
        await cache.GetStringAsync(GenerationKey, cancellationToken) ?? InitialGeneration;
}
