using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Plans.Models;

/// <summary>
/// The value stored per feature key in the features JSON snapshot: <c>"true"</c> for a plain feature, or
/// <c>"&lt;limit&gt;/&lt;Period&gt;"</c> (e.g. <c>"1000/Monthly"</c>) for a metered one. A metered feature is also enabled.
/// </summary>
public static class FeatureValue
{
    public const string On = "true";

    public static string For(PlanFeature feature) =>
        feature.Limit is { } limit
            ? Format(limit, feature.ResetPeriod ?? QuotaResetPeriod.Monthly)
            : On;

    public static string Format(long limit, QuotaResetPeriod period) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{limit}/{period}");

    /// <summary>True for <c>"true"</c> and for any metered value.</summary>
    public static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var on) ? on : TryParseQuota(value, out _, out _);

    public static bool TryParseQuota(string? value, out long limit, out QuotaResetPeriod period)
    {
        limit = 0;
        period = QuotaResetPeriod.Monthly;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var slash = value.IndexOf('/');
        if (slash <= 0)
            return false;

        return long.TryParse(value.AsSpan(0, slash), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out limit) &&
               Enum.TryParse(value.AsSpan(slash + 1), ignoreCase: false, out period) &&
               Enum.IsDefined(period);
    }
}
