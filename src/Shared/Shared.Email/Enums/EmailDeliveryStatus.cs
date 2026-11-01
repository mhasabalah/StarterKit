using System.Text.Json.Serialization;

namespace Shared.Email.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailDeliveryStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2
}
