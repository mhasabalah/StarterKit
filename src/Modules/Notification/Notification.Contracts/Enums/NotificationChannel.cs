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

    /// <summary>
    /// The in-app inbox (+ real-time SignalR push to open sessions). ADDITIVE: delivered alongside the fallback chain,
    /// never part of a user's channel order.
    /// </summary>
    [LocalizedDescription("In-app", "داخل التطبيق")]
    InApp = 4,

    /// <summary>Mobile / web push through FCM (Android, iOS via APNs, Web). ADDITIVE like <see cref="InApp"/>.</summary>
    [LocalizedDescription("Push notification", "إشعار فوري")]
    Push = 5,
}
