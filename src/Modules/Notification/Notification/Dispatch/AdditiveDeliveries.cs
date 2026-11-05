using Notification.Contracts.InApp;
using Notification.Contracts.Push;

namespace Notification.Dispatch;

/// <summary>Hangfire argument of <see cref="InAppDeliveryJob"/>: one inbox entry for one user.</summary>
public sealed record InAppDelivery(Guid UserId, NotificationCulture Culture, InAppContent Content);

/// <summary>
/// Hangfire argument of <see cref="PushDeliveryJob"/>. <see cref="DeviceIds"/> null = every active device of the user
/// (first attempt); a follow-up attempt carries only the devices that failed transiently, so a device that already got
/// the message is never sent it again by a retry.
/// </summary>
public sealed record PushDelivery(
    Guid UserId,
    NotificationCulture Culture,
    PushContent Content,
    IReadOnlyList<Guid>? DeviceIds = null,
    int Attempt = 1);

/// <summary>Channels delivered alongside (not as part of) the email/WhatsApp fallback chain.</summary>
public static class AdditiveChannels
{
    public static bool IsAdditive(this NotificationChannel channel) =>
        channel is NotificationChannel.InApp or NotificationChannel.Push;

    /// <summary>Picks the text for the recipient's culture; an empty Arabic text falls back to English.</summary>
    public static string For(this LocalizedText text, NotificationCulture culture) =>
        culture == NotificationCulture.Ar && !string.IsNullOrWhiteSpace(text.Ar) ? text.Ar : text.En ?? string.Empty;
}
