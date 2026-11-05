using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Notification.Contracts.Enums;

/// <summary>How an in-app notification is presented (icon / colour); never changes delivery.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationSeverity
{
    [LocalizedDescription("Info", "معلومة")]
    Info = 0,

    [LocalizedDescription("Success", "نجاح")]
    Success = 1,

    [LocalizedDescription("Warning", "تحذير")]
    Warning = 2,

    [LocalizedDescription("Critical", "حرج")]
    Critical = 3,
}
