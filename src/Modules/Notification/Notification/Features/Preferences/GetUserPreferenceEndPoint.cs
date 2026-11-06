namespace Notification.Features.Preferences;

public sealed class GetUserPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/notifications/preferences/{userId:guid}",
                async (
                    Guid userId,
                    NotificationDbContext db,
                    IOptions<NotificationOptions> options,
                    CancellationToken ct) =>
                {
                    var preference = await db.UserNotificationPreferences
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.UserId == userId, ct);

                    var result = PreferenceRules.ToDto(userId, preference, options.Value.DefaultChannelOrder);

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetUserNotificationPreference")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<MyPreferenceDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get user notification preference (Admin)")
            .WithDescription("Admin: returns any user's channel preference.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
