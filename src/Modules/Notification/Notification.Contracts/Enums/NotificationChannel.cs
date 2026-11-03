using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Notification.Contracts.Enums;

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
