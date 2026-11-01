namespace Shared.Notification.Endpoints;

public sealed class ReEnqueueNotificationEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/{id:guid}/reenqueue",
                async (Guid id, INotificationJobEnqueuer enqueuer, CancellationToken ct) =>
                {
                    try
                    {
                        var newJobId = await enqueuer.ReEnqueueAsync(id, ct: ct);
                        return Results.Ok(ApiResponseExtensions.Success(new
                        {
                            NotificationLogId = id,
                            NewJobId = newJobId,
                            Message = "Re-enqueued successfully"
                        }));
                    }
                    catch (NotFoundException ex)
                    {
                        return ApiResponseExtensions.NotFound(ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return ApiResponseExtensions.BadRequest(ex.Message);
                    }
                })
            .WithName("ReEnqueueNotification")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Re-enqueue failed notification")
            .WithDescription("Admin: re-enqueue a failed notification log record for retry on the same channel.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
