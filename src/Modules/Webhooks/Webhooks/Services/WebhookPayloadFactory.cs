using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.Contracts.Messaging;

namespace Webhooks.Services;

/// <summary>
/// Builds the JSON body customers receive:
/// <c>{ "id", "type", "timestamp", "tenantId", "data": { ...event properties... } }</c>. <c>id</c> is the integration event's
/// <c>EventId</c> (the same value as the <c>webhook-id</c> header minus its prefix), so receivers can de-duplicate on it.
/// </summary>
public static class WebhookPayloadFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Create(string eventType, IIntegrationEvent integrationEvent, Guid tenantId)
    {
        var data = JsonSerializer.SerializeToNode(integrationEvent, integrationEvent.GetType(), Json) as JsonObject
                   ?? new JsonObject();
        // Envelope fields replace the transport metadata.
        data.Remove("eventId");
        data.Remove("occurredOn");

        return Envelope(integrationEvent.EventId, eventType, integrationEvent.OccurredOn, tenantId, data);
    }

    /// <summary>The payload of a manually sent test ping.</summary>
    public static string CreatePing(Guid eventId, DateTime now, Guid tenantId) =>
        Envelope(eventId, PingEventType, now, tenantId, new JsonObject { ["message"] = "Webhook test ping." });

    public const string PingEventType = "webhook.ping";

    private static string Envelope(Guid id, string type, DateTime occurredOn, Guid tenantId, JsonObject data)
    {
        var envelope = new JsonObject
        {
            ["id"] = $"evt_{id:N}",
            ["type"] = type,
            ["timestamp"] = DateTime.SpecifyKind(occurredOn, DateTimeKind.Utc).ToString("O"),
            ["tenantId"] = tenantId,
            ["data"] = data
        };
        return envelope.ToJsonString(Json);
    }
}
