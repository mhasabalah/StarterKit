using System.Text.Json.Serialization;
using Shared.Core.Attributes;

namespace Shared.Notification.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationChannel
{
    [LocalizedDescription("WhatsApp", "واتساب")]
    WhatsApp = 1,

    [LocalizedDescription("Email", "البريد الإلكتروني")]
    Email = 2,

    [LocalizedDescription("SMS", "رسالة نصية")]
    Sms = 3,
}
