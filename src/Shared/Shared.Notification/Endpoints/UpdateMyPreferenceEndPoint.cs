namespace Shared.Notification.Endpoints;

public sealed record UpdatePreferenceRequest(NotificationChannel[] ChannelOrder);

public sealed record UpdatePreferenceResponse(
    Guid UserId,
    NotificationChannel[] ChannelOrder,
    DateTime UpdatedAt);

public sealed class UpdateMyPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/notifications/preferences",
                async (
                    UpdatePreferenceRequest request,
                    IHttpContextService httpCtx,
                    NotificationDbContext db,
                    CancellationToken ct) =>
                {
                    if (request.ChannelOrder is not { Length: > 0 })
                        return ApiResponseExtensions.BadRequest("channelOrder must not be empty.");

                    if (request.ChannelOrder.Distinct().Count() != request.ChannelOrder.Length)
                        return ApiResponseExtensions.BadRequest("channelOrder must not contain duplicates.");

                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");
                    var now = DateTime.UtcNow;

                    var existing = await db.UserNotificationPreferences
                        .FirstOrDefaultAsync(x => x.UserId == userId, ct);

                    if (existing is null)
                    {
                        db.UserNotificationPreferences.Add(new UserNotificationPreference
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            ChannelOrder = request.ChannelOrder,
                            UpdatedAt = now
                        });
                    }
                    else
                    {
                        existing.ChannelOrder = request.ChannelOrder;
                        existing.UpdatedAt = now;
                    }

                    await db.SaveChangesAsync(ct);

                    return Results.Ok(ApiResponseExtensions.Success(
                        new UpdatePreferenceResponse(userId, request.ChannelOrder, now)));
                })
            .WithName("UpdateMyNotificationPreference")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<UpdatePreferenceResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Update my notification preference")
            .WithDescription("Sets the calling user's preferred channel order.")
            .WithOpenApi()
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
