using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace Shared.Notification.Endpoints;

public sealed record NotificationLogDto(
    Guid Id,
    Guid CorrelationId,
    Guid UserId,
    NotificationChannel Channel,
    string TemplateName,
    NotificationDeliveryStatus Status,
    string? Error,
    string? HangfireJobId,
    DateTime CreatedAt,
    DateTime? SentAt);

public record NotificationHistoryRequest : BaseQuery
{
    public Guid UserId { get; init; }
    public NotificationChannel? Channel { get; init; }
    public NotificationDeliveryStatus? Status { get; init; }
}

public sealed class GetNotificationHistoryEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/notifications/history",
                async (
                    [AsParameters] NotificationHistoryRequest request,
                    NotificationDbContext db,
                    CancellationToken ct) =>
                {
                    var queryable = db.NotificationLogs.AsNoTracking()
                        .Where(x => x.UserId == request.UserId);

                    if (request.Channel.HasValue)
                        queryable = queryable.Where(x => x.Channel == request.Channel.Value);

                    if (request.Status.HasValue)
                        queryable = queryable.Where(x => x.Status == request.Status.Value);

                    queryable = queryable.OrderByDescending(x => x.CreatedAt);
                    queryable = queryable.OrderByDynamic(request.SortColumn, request.SortBy);

                    var dtoQuery = queryable.Select(x => new NotificationLogDto(
                        x.Id, x.CorrelationId, x.UserId, x.Channel,
                        x.TemplateName, x.Status, x.Error, x.HangfireJobId,
                        x.CreatedAt, x.SentAt));

                    var result = await dtoQuery.PagedResult(request.PageNumber, request.PageSize, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("search")
            .WithName("GetNotificationHistory")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<PaginatedResult<NotificationLogDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Notification History")
            .WithDescription("Admin: paginated notification log for a user, filterable by channel and status.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
