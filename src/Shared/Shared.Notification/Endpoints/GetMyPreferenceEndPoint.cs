namespace Shared.Notification.Endpoints;

public sealed record MyPreferenceDto(
    Guid UserId,
    NotificationChannel[] ChannelOrder,
    bool IsDefault);

public sealed class GetMyPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/notifications/preferences",
                async (
                    IHttpContextService httpCtx,
                    NotificationDbContext db,
                    IOptions<NotificationOptions> options,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var preference = await db.UserNotificationPreferences
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.UserId == userId, ct);

                    var result = preference is not null
                        ? new MyPreferenceDto(userId, preference.ChannelOrder, IsDefault: false)
                        : new MyPreferenceDto(userId, options.Value.DefaultChannelOrder, IsDefault: true);

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetMyNotificationPreference")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<MyPreferenceDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Get my notification preference")
            .WithDescription("Returns the calling user's channel preference, or system default if none set.")
            .WithOpenApi()
            .RequireAuthorization()
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
