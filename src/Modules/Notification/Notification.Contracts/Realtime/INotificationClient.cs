using Notification.Contracts.Inbox;

namespace Notification.Contracts.Realtime;

/// <summary>
/// Server → client events of the notification hub (<see cref="NotificationHubContract.Path"/>). The hub is push-only:
/// clients invoke nothing on it; every command (mark read, delete, ...) goes through the REST API. Events are hints for
/// the UI - delivery is best effort, so a client re-reads the unread count over REST after (re)connecting.
/// </summary>
public interface INotificationClient
{
    /// <summary>A new inbox entry for the user. May repeat after a server retry: de-duplicate by <c>Id</c>.</summary>
    Task NotificationReceived(InboxNotificationDto notification);

    /// <summary>The user's unread count changed (in <see cref="UnreadCountChangedEvent.TenantId"/>'s inbox scope).</summary>
    Task UnreadCountChanged(UnreadCountChangedEvent change);

    /// <summary>Entries were marked read on another tab/device.</summary>
    Task NotificationsRead(NotificationsReadEvent read);

    /// <summary>An entry was deleted on another tab/device.</summary>
    Task NotificationDeleted(NotificationDeletedEvent deleted);

    /// <summary>A transient event from another module (e.g. job progress). Not stored: re-read over REST on reconnect.</summary>
    Task UserEvent(UserEvent userEvent);
}

/// <summary>Hub route and client method names (for non-.NET clients).</summary>
public static class NotificationHubContract
{
    public const string Path = "/hubs/notifications";

    public const string NotificationReceived = nameof(INotificationClient.NotificationReceived);
    public const string UnreadCountChanged = nameof(INotificationClient.UnreadCountChanged);
    public const string NotificationsRead = nameof(INotificationClient.NotificationsRead);
    public const string NotificationDeleted = nameof(INotificationClient.NotificationDeleted);
    public const string UserEvent = nameof(INotificationClient.UserEvent);
}

/// <param name="TenantId">Inbox scope the count belongs to (null = no tenant / single-tenant).</param>
/// <param name="Count">Unread entries in that scope.</param>
public sealed record UnreadCountChangedEvent(Guid? TenantId, int Count);

/// <param name="Ids">The entries marked read; null = "all up to <see cref="UpTo"/>".</param>
/// <param name="UpTo">With <see cref="Ids"/> null: every entry created at or before this time is now read.</param>
public sealed record NotificationsReadEvent(IReadOnlyCollection<Guid>? Ids, DateTime ReadAt, DateTime? UpTo);

public sealed record NotificationDeletedEvent(Guid Id);
