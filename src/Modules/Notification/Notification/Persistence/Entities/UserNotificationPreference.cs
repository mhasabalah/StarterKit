namespace Notification.Persistence.Entities;

public sealed class UserNotificationPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel[] ChannelOrder { get; set; } = [];

    /// <summary>False = the user turned push notifications off (the in-app inbox is always kept).</summary>
    public bool PushEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
