using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Notification.Contracts.Inbox;
using Notification.Contracts.Realtime;

namespace Notification.Realtime;

/// <summary>
/// The real-time notification hub (<see cref="NotificationHubContract.Path"/>). Push-only by design: it exposes no
/// methods, so there is no hub-side input to validate, authorize or rate-limit; commands go through REST (mediator
/// pipeline: validation, concurrency, tenant scope). A connection is addressed by its USER id
/// (<see cref="NotificationHubUserIdProvider"/>), so every tab and device of the user receives the events. Each
/// connection is tracked in <see cref="HubConnectionRegistry"/> so a revoked session's socket can be closed at once.
/// </summary>
[Authorize]
public sealed class NotificationHub(HubConnectionRegistry connections, ILogger<NotificationHub> logger) : Hub<INotificationClient>
{
    public override Task OnConnectedAsync()
    {
        connections.Add(Context);
        logger.LogDebug("Notification hub connected. User: {UserId} Connection: {ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        logger.LogDebug(exception, "Notification hub disconnected. User: {UserId} Connection: {ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// The SignalR user id = the canonical (<c>"D"</c>-formatted) Guid of the <c>NameIdentifier</c> claim - the claim
/// <c>IHttpContextService.GetUserIdAsGuid</c> reads - so <c>Clients.User(userId.ToString())</c> always matches,
/// whatever casing the token carries. API keys never reach the hub (<c>DisallowApiKey</c>).
/// </summary>
public sealed class NotificationHubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        Guid.TryParse(connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id.ToString("D")
            : null;
}

/// <summary>Port the inbox uses to tell a user's open sessions about changes. Best effort: never throws.</summary>
public interface IRealtimeNotifier
{
    Task NotificationReceivedAsync(Guid userId, InboxNotificationDto notification, CancellationToken ct = default);

    Task UnreadCountChangedAsync(Guid userId, Guid? tenantId, int count, CancellationToken ct = default);

    Task NotificationsReadAsync(Guid userId, NotificationsReadEvent read, CancellationToken ct = default);

    Task NotificationDeletedAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IRealtimeNotifier"/> over <see cref="IHubContext{THub,T}"/>. With the Redis backplane the send reaches
/// sockets on every replica. Failures (Redis down, ...) are logged and swallowed: the inbox row is the source of
/// truth and clients re-read it over REST on reconnect, so a lost event costs freshness, never data.
/// </summary>
public sealed class SignalRRealtimeNotifier(
    IHubContext<NotificationHub, INotificationClient> hub,
    NotificationMetrics metrics,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task NotificationReceivedAsync(Guid userId, InboxNotificationDto notification, CancellationToken ct = default) =>
        SendAsync(userId, NotificationHubContract.NotificationReceived, c => c.NotificationReceived(notification));

    public Task UnreadCountChangedAsync(Guid userId, Guid? tenantId, int count, CancellationToken ct = default) =>
        SendAsync(userId, NotificationHubContract.UnreadCountChanged, c => c.UnreadCountChanged(new UnreadCountChangedEvent(tenantId, count)));

    public Task NotificationsReadAsync(Guid userId, NotificationsReadEvent read, CancellationToken ct = default) =>
        SendAsync(userId, NotificationHubContract.NotificationsRead, c => c.NotificationsRead(read));

    public Task NotificationDeletedAsync(Guid userId, Guid notificationId, CancellationToken ct = default) =>
        SendAsync(userId, NotificationHubContract.NotificationDeleted, c => c.NotificationDeleted(new NotificationDeletedEvent(notificationId)));

    private async Task SendAsync(Guid userId, string method, Func<INotificationClient, Task> send)
    {
        try
        {
            await send(hub.Clients.User(userId.ToString("D")));
            metrics.RealtimeSent(method, success: true);
        }
        catch (Exception ex)
        {
            metrics.RealtimeSent(method, success: false);
            logger.LogWarning(ex, "Real-time {Method} to user {UserId} failed (clients resync over REST)", method, userId);
        }
    }
}

/// <summary>
/// <see cref="IUserEventPublisher"/> over the hub (best effort, like the inbox events). The event is checked against the
/// contract limits before it is sent; an invalid one is dropped with a warning, never thrown.
/// </summary>
public sealed class SignalRUserEventPublisher(
    IHubContext<NotificationHub, INotificationClient> hub,
    NotificationMetrics metrics,
    ILogger<SignalRUserEventPublisher> logger) : IUserEventPublisher
{
    public async Task PublishAsync(Guid userId, UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEvent.Type) || userEvent.Type.Length > UserEvent.TypeMaxLength ||
            userEvent.Data.Count > UserEvent.MaxDataEntries)
        {
            logger.LogWarning("User event {Type} for {UserId} dropped: outside the contract limits", userEvent.Type, userId);
            return;
        }

        try
        {
            await hub.Clients.User(userId.ToString("D")).UserEvent(userEvent);
            metrics.RealtimeSent(NotificationHubContract.UserEvent, success: true);
        }
        catch (Exception ex)
        {
            metrics.RealtimeSent(NotificationHubContract.UserEvent, success: false);
            logger.LogWarning(ex, "User event {Type} to user {UserId} failed (clients resync over REST)", userEvent.Type, userId);
        }
    }
}

/// <summary>Used when <c>Realtime:Enabled = false</c>: no hub, clients poll the unread count.</summary>
public sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    public Task NotificationReceivedAsync(Guid userId, InboxNotificationDto notification, CancellationToken ct = default) => Task.CompletedTask;

    public Task UnreadCountChangedAsync(Guid userId, Guid? tenantId, int count, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotificationsReadAsync(Guid userId, NotificationsReadEvent read, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotificationDeletedAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => Task.CompletedTask;
}
