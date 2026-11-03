using System.Text.Json;
using System.Text.Json.Serialization;
using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Domains.Subscriptions.Services;

internal sealed record SubscriptionCachePayload(
    [property: JsonPropertyName("maxItems")] int? MaxItems,
    [property: JsonPropertyName("maxStorageGb")] int? MaxStorageGb,
    [property: JsonPropertyName("maxUsers")] int? MaxUsers,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("gracePeriodEndsAt")] DateTimeOffset? GracePeriodEndsAt,
    [property: JsonPropertyName("features")] Dictionary<string, string> Features)
{
    public ActivePlanLimitsDto ToLimitsDto()
    {
        return new ActivePlanLimitsDto(MaxItems, MaxStorageGb, MaxUsers, Status, GracePeriodEndsAt);
    }
}

internal static class SubscriptionCacheSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string SerializePayload(SubscriptionCachePayload payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    public static SubscriptionCachePayload? DeserializePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SubscriptionCachePayload>(json, JsonOptions);
            return payload is null || string.IsNullOrWhiteSpace(payload.Status) ? null : payload;
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> ExtractFeatures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var payload = DeserializePayload(json);
        if (payload is not null)
        {
            return payload.Features;
        }

        return FeaturesJson.Deserialize(json);
    }

    public static SubscriptionCachePayload CreatePayload(
        ActivePlanLimitsDto limits,
        Dictionary<string, string> features)
    {
        return new SubscriptionCachePayload(
            limits.MaxItems,
            limits.MaxStorageGb,
            limits.MaxUsers,
            limits.Status,
            limits.GracePeriodEndsAt,
            features);
    }
}
