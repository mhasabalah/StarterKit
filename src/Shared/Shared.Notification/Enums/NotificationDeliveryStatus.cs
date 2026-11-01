using System.Text.Json.Serialization;
using Shared.Core.Attributes;

namespace Shared.Notification.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationDeliveryStatus
{
    [LocalizedDescription("Queued", "في الانتظار")]
    Queued = 0,

    [LocalizedDescription("Sent", "مُرسَل")]
    Sent = 1,

    [LocalizedDescription("Failed", "فشل")]
    Failed = 2,

    [LocalizedDescription("Skipped", "تم التخطي")]
    Skipped = 3,
}
