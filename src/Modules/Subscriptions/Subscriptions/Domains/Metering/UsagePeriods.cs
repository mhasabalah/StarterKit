using System.Globalization;
using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Metering;

/// <summary>Counter periods: calendar months (UTC) or one never-resetting counter.</summary>
public static class UsagePeriods
{
    public const string AllTime = "all";

    public static string KeyFor(QuotaResetPeriod period, DateTimeOffset now) => period == QuotaResetPeriod.Never
        ? AllTime
        : now.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Start of the next period, or null when the counter never resets.</summary>
    public static DateTimeOffset? ResetsAt(QuotaResetPeriod period, DateTimeOffset now)
    {
        if (period == QuotaResetPeriod.Never)
            return null;

        var utc = now.UtcDateTime;
        return new DateTimeOffset(new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc)).AddMonths(1);
    }

    /// <summary>Counter key in the store: <c>usage:{subscriber}:{period}:{feature}</c> (the feature may contain colons).</summary>
    public static string CounterKey(Guid subscriberId, string periodKey, string featureKey) =>
        $"usage:{subscriberId:N}:{periodKey}:{featureKey}";

    public static bool TryParseCounterKey(string counterKey, out Guid subscriberId, out string periodKey, out string featureKey)
    {
        subscriberId = Guid.Empty;
        periodKey = featureKey = string.Empty;

        var parts = counterKey.Split(':', 4);
        if (parts.Length != 4 || parts[0] != "usage" || !Guid.TryParseExact(parts[1], "N", out subscriberId))
            return false;

        periodKey = parts[2];
        featureKey = parts[3];
        return featureKey.Length > 0;
    }
}
