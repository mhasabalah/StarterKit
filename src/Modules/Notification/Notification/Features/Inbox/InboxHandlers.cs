using Notification.Contracts.Inbox;
using Notification.Contracts.Realtime;

namespace Notification.Features.Inbox;

public sealed class InboxNotificationNotFoundException(Guid id) : NotFoundException("InboxNotification", id);

/// <summary>The calling user, or 401. Inbox and device endpoints only ever touch the caller's own rows.</summary>
internal static class Caller
{
    public static Guid UserId(IHttpContextService http) =>
        http.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");
}

// ----- list -----------------------------------------------------------------------------------------------------

public class GetInboxNotificationsQueryValidator : AbstractValidator<GetInboxNotificationsQuery>
{
    public GetInboxNotificationsQueryValidator(IStringLocalizer<NotificationResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Category).MaximumLength(Contracts.InApp.InAppContent.CategoryMaxLength)
            .WithMessage(_ => localizer["Validation.Category.MaxLength"]);
    }
}

public class GetInboxNotificationsHandler(NotificationDbContext db, IHttpContextService http, ITenantContext tenantContext)
    : IQueryHandler<GetInboxNotificationsQuery, PaginatedResult<InboxNotificationDto>>
{
    public Task<PaginatedResult<InboxNotificationDto>> Handle(GetInboxNotificationsQuery query, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var entries = db.InboxNotifications.AsNoTracking().VisibleTo(userId, InboxScope.CurrentTenantId(tenantContext));

        if (query.UnreadOnly == true)
            entries = entries.Where(x => x.ReadAt == null);
        if (!string.IsNullOrWhiteSpace(query.Category))
            entries = entries.Where(x => x.Category == query.Category);

        // Fixed order (newest first, id as tie-breaker): stable pages while new entries arrive.
        return entries
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new InboxNotificationDto(x.Id, x.CorrelationId, x.TenantId, x.Category, x.Severity, x.Title, x.Body,
                x.ActionUrl, x.Data, x.CreatedAt, x.ReadAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

// ----- unread count ---------------------------------------------------------------------------------------------

public class GetUnreadInboxCountHandler(IInboxNotificationRepository inbox, IHttpContextService http, ITenantContext tenantContext)
    : IQueryHandler<GetUnreadInboxCountQuery, GetUnreadInboxCountResult>
{
    public async Task<GetUnreadInboxCountResult> Handle(GetUnreadInboxCountQuery query, CancellationToken cancellationToken) =>
        new(await inbox.CountUnreadAsync(Caller.UserId(http), InboxScope.CurrentTenantId(tenantContext), cancellationToken));
}

// ----- mark one read --------------------------------------------------------------------------------------------

public class MarkInboxNotificationReadCommandValidator : AbstractValidator<MarkInboxNotificationReadCommand>
{
    public MarkInboxNotificationReadCommandValidator(IStringLocalizer<NotificationResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class MarkInboxNotificationReadHandler(
    IInboxNotificationRepository inbox,
    IRealtimeNotifier realtime,
    IHttpContextService http,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ICommandHandler<MarkInboxNotificationReadCommand, MarkInboxNotificationReadResult>
{
    public async Task<MarkInboxNotificationReadResult> Handle(MarkInboxNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var tenantId = InboxScope.CurrentTenantId(tenantContext);
        var entry = await inbox.GetForUserAsync(command.Id, userId, tenantId, cancellationToken)
                    ?? throw new InboxNotificationNotFoundException(command.Id);

        if (entry.ReadAt is { } readAt)
            return new MarkInboxNotificationReadResult(entry.Id, readAt, await inbox.CountUnreadAsync(userId, tenantId, cancellationToken));

        entry.ReadAt = timeProvider.GetUtcNow().UtcDateTime;
        await inbox.SaveChangesAsync(cancellationToken);

        var unread = await inbox.CountUnreadAsync(userId, tenantId, cancellationToken);
        await realtime.NotificationsReadAsync(userId, new NotificationsReadEvent([entry.Id], entry.ReadAt.Value, null), cancellationToken);
        await realtime.UnreadCountChangedAsync(userId, tenantId, unread, cancellationToken);
        return new MarkInboxNotificationReadResult(entry.Id, entry.ReadAt.Value, unread);
    }
}

// ----- mark all read --------------------------------------------------------------------------------------------

public class MarkAllInboxNotificationsReadCommandValidator : AbstractValidator<MarkAllInboxNotificationsReadCommand>
{
    public MarkAllInboxNotificationsReadCommandValidator(IStringLocalizer<NotificationResource> localizer) =>
        RuleFor(x => x.UpTo).Must(v => v is null || v.Value > DateTime.MinValue)
            .WithMessage(_ => localizer["Validation.UpTo.Invalid"]);
}

/// <summary>
/// One set-based UPDATE (no load/save loop, no lost update with a concurrent single read). Entries newer than
/// <c>UpTo</c> (arrived after the user's click) stay unread.
/// </summary>
public class MarkAllInboxNotificationsReadHandler(
    IInboxNotificationRepository inbox,
    IRealtimeNotifier realtime,
    IHttpContextService http,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ICommandHandler<MarkAllInboxNotificationsReadCommand, MarkAllInboxNotificationsReadResult>
{
    public async Task<MarkAllInboxNotificationsReadResult> Handle(MarkAllInboxNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var tenantId = InboxScope.CurrentTenantId(tenantContext);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var upTo = command.UpTo is { } requested ? Min(AsUtc(requested), now) : now;

        var marked = await inbox.MarkAllReadAsync(userId, tenantId, upTo, now, cancellationToken);
        var unread = await inbox.CountUnreadAsync(userId, tenantId, cancellationToken);

        if (marked > 0)
        {
            await realtime.NotificationsReadAsync(userId, new NotificationsReadEvent(null, now, upTo), cancellationToken);
            await realtime.UnreadCountChangedAsync(userId, tenantId, unread, cancellationToken);
        }

        return new MarkAllInboxNotificationsReadResult(marked, unread);
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    /// <summary>A timestamp without offset is UTC (the API speaks UTC); never reinterpret it as server-local time.</summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

// ----- delete ---------------------------------------------------------------------------------------------------

public class DeleteInboxNotificationCommandValidator : AbstractValidator<DeleteInboxNotificationCommand>
{
    public DeleteInboxNotificationCommandValidator(IStringLocalizer<NotificationResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class DeleteInboxNotificationHandler(
    IInboxNotificationRepository inbox,
    IRealtimeNotifier realtime,
    IHttpContextService http,
    ITenantContext tenantContext) : ICommandHandler<DeleteInboxNotificationCommand, DeleteInboxNotificationResult>
{
    public async Task<DeleteInboxNotificationResult> Handle(DeleteInboxNotificationCommand command, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var tenantId = InboxScope.CurrentTenantId(tenantContext);
        var entry = await inbox.GetForUserAsync(command.Id, userId, tenantId, cancellationToken)
                    ?? throw new InboxNotificationNotFoundException(command.Id);

        var wasUnread = entry.ReadAt is null;
        inbox.Remove(entry);
        await inbox.SaveChangesAsync(cancellationToken);

        var unread = await inbox.CountUnreadAsync(userId, tenantId, cancellationToken);
        await realtime.NotificationDeletedAsync(userId, entry.Id, cancellationToken);
        if (wasUnread)
            await realtime.UnreadCountChangedAsync(userId, tenantId, unread, cancellationToken);

        return new DeleteInboxNotificationResult(entry.Id, unread);
    }
}
