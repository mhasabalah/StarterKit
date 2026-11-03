namespace Notification.Channels;

public interface INotificationChannelAdapter
{
    NotificationChannel Channel { get; }

    bool CanSend(string? email, string? phone);

    Task SendAsync(
        NotificationMessage message,
        string? email,
        string? phone,
        CancellationToken ct);
}
