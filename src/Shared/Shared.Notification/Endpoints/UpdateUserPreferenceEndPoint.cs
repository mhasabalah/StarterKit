namespace Shared.Notification.Endpoints;

public sealed class UpdateUserPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/notifications/preferences/{userId:guid}",
                async (
                    Guid userId,
                    UpdatePreferenceRequest request,
                    NotificationDbContext db,
                    CancellationToken ct) =>
                {
                    if (request.ChannelOrder is not { Length: > 0 })
                        return ApiResponseExtensions.BadRequest("channelOrder must not be empty.");

                    if (request.ChannelOrder.Distinct().Count() != request.ChannelOrder.Length)
                        return ApiResponseExtensions.BadRequest("channelOrder must not contain duplicates.");

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
            .WithName("UpdateUserNotificationPreference")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<UpdatePreferenceResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Update user notification preference (Admin)")
            .WithDescription("Admin: sets any user's preferred channel order.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
