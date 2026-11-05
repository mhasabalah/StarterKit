using System.Text.Json;

namespace Subscriptions.Domains.Plans.Models;

/// <summary>
/// The ONE serializer for the features JSON snapshotted on subscriptions and scheduled plan changes:
/// <c>{"feature.key":"true", ...}</c>. Previously hand-rolled in four handlers/jobs.
/// </summary>
public static class FeaturesJson
{
    public const string Empty = "{}";

    public static string FromPlan(Plan plan) =>
        JsonSerializer.Serialize(plan.Features
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => FeatureValue.For(g.First())));

    public static string Serialize(IEnumerable<string> enabledKeys) =>
        JsonSerializer.Serialize(enabledKeys.Distinct(StringComparer.Ordinal).ToDictionary(x => x, _ => "true"));

    /// <summary>Lenient read: invalid or empty JSON yields an empty dictionary.</summary>
    public static Dictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
