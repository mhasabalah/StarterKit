using System.Text.Json.Serialization;

namespace Notification.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationCulture
{
    En = 0,
    Ar = 1,
}
