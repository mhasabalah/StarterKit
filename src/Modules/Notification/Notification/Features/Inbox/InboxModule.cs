using Notification.Contracts.Inbox;

namespace Notification.Features.Inbox;

/// <summary>
/// The caller's in-app inbox. Every route is per user (the caller's own entries only; a foreign id is 404, never 403,
/// so ids cannot be probed). Real-time updates of the same data arrive on the notification hub.
/// </summary>
public sealed class InboxModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/notifications/inbox")
            .WithTags("Notifications")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .HasApiVersion(1.0);

        group.MapGet("/", async ([AsParameters] GetInboxNotificationsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetInboxNotifications")
            .Produces<ApiResponse<PaginatedResult<InboxNotificationDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("My inbox")
            .WithDescription("The caller's in-app notifications, newest first. Filters: unreadOnly, category.")
            .RequireAuthorization()
            .RequireRateLimiting("search");

        group.MapGet("/unread-count", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetUnreadInboxCountQuery(), ct))))
            .WithName("GetUnreadInboxCount")
            .Produces<ApiResponse<GetUnreadInboxCountResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("My unread count")
            .WithDescription("Unread in-app notifications of the caller (badge). Re-read it after (re)connecting to the hub.")
            .RequireAuthorization()
            .RequireRateLimiting("search");

        group.MapPost("/{id:guid}/read", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new MarkInboxNotificationReadCommand(id), ct))))
            .WithName("MarkInboxNotificationRead")
            .Produces<ApiResponse<MarkInboxNotificationReadResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Mark read")
            .WithDescription("Idempotent: an entry that is already read keeps its first read time.")
            .RequireAuthorization()
            .RequireRateLimiting("create");

        group.MapPost("/read-all", async (MarkAllInboxNotificationsReadRequest? request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(
                    await sender.Send(new MarkAllInboxNotificationsReadCommand(request?.UpTo), ct))))
            .WithName("MarkAllInboxNotificationsRead")
            .Produces<ApiResponse<MarkAllInboxNotificationsReadResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Mark all read")
            .WithDescription("Marks unread entries created at or before upTo (default: now) read. Pass the createdAt of the " +
                             "newest entry the user has SEEN, so entries that arrive meanwhile stay unread.")
            .RequireAuthorization()
            .RequireRateLimiting("create");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteInboxNotificationCommand(id), ct))))
            .WithName("DeleteInboxNotification")
            .Produces<ApiResponse<DeleteInboxNotificationResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete")
            .WithDescription("Removes one entry from the caller's inbox.")
            .RequireAuthorization()
            .RequireRateLimiting("create");
    }
}
