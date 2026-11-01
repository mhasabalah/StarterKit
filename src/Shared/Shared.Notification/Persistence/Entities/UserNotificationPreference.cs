namespace Shared.Notification.Persistence;

public sealed class UserNotificationPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel[] ChannelOrder { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
