namespace Shared.Notification.Options;

public sealed class NotificationOptions
{
    public NotificationChannel[] DefaultChannelOrder { get; set; } =
        [NotificationChannel.WhatsApp, NotificationChannel.Email];
}
