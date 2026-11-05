namespace Notification.Contracts.Inbox;

/// <summary>One inbox entry of the calling user (text already in the recipient's language).</summary>
public sealed record InboxNotificationDto(
    Guid Id,
    Guid CorrelationId,
    Guid? TenantId,
    string Category,
    NotificationSeverity Severity,
    string Title,
    string Body,
    string? ActionUrl,
    IReadOnlyDictionary<string, string>? Data,
    DateTime CreatedAt,
    DateTime? ReadAt);

/// <summary>The caller's inbox, newest first. Scoped to the current tenant (plus tenant-less platform notices).</summary>
public sealed record GetInboxNotificationsQuery : BaseQuery, IQuery<PaginatedResult<InboxNotificationDto>>
{
    /// <summary>Only unread entries.</summary>
    public bool? UnreadOnly { get; init; }

    /// <summary>Only this category (exact match).</summary>
    public string? Category { get; init; }
}

/// <summary>Unread entries of the caller (current tenant + platform notices).</summary>
public sealed record GetUnreadInboxCountQuery : IQuery<GetUnreadInboxCountResult>;

public sealed record GetUnreadInboxCountResult(int Count);

/// <summary>Marks one of the caller's entries read. Idempotent: an already-read entry keeps its first read time.</summary>
public sealed record MarkInboxNotificationReadCommand(Guid Id) : ICommand<MarkInboxNotificationReadResult>;

public sealed record MarkInboxNotificationReadResult(Guid Id, DateTime ReadAt, int UnreadCount);

/// <summary>
/// Marks the caller's unread entries read. <see cref="UpTo"/> (the newest entry the client has SEEN) keeps entries that
/// arrived after the user clicked "mark all read" unread; omit it to mark everything up to now.
/// </summary>
public sealed record MarkAllInboxNotificationsReadCommand(DateTime? UpTo = null) : ICommand<MarkAllInboxNotificationsReadResult>;

public sealed record MarkAllInboxNotificationsReadResult(int Marked, int UnreadCount);

/// <summary>Deletes (dismisses) one of the caller's entries. A foreign or unknown id is 404.</summary>
public sealed record DeleteInboxNotificationCommand(Guid Id) : ICommand<DeleteInboxNotificationResult>;

public sealed record DeleteInboxNotificationResult(Guid Id, int UnreadCount);

/// <summary>Request body of "mark all read".</summary>
public sealed record MarkAllInboxNotificationsReadRequest(DateTime? UpTo);
