namespace Notification.Features.Preferences;

/// <param name="ChannelOrder">Email/WhatsApp fallback order (in-app and push are not part of it).</param>
/// <param name="PushEnabled">Turn push notifications on/off; omit to keep the current setting.</param>
public sealed record UpdatePreferenceRequest(NotificationChannel[] ChannelOrder, bool? PushEnabled = null);

public sealed record UpdatePreferenceResponse(
    Guid UserId,
    NotificationChannel[] ChannelOrder,
    DateTime UpdatedAt,
    bool PushEnabled);

public sealed class UpdateMyPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/notifications/preferences",
                async (
                    UpdatePreferenceRequest request,
                    IHttpContextService httpCtx,
                    INotificationPreferenceRepository preferences,
                    IEnumerable<INotificationChannelAdapter> adapters,
                    CancellationToken ct) =>
                {
                    var validationError = ChannelOrderRules.Validate(request.ChannelOrder, adapters);
                    if (validationError is not null)
                        return ApiResponseExtensions.BadRequest(validationError);

                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var now = await preferences.UpsertAsync(userId, request.ChannelOrder, request.PushEnabled, ct);
                    var pushEnabled = request.PushEnabled ?? await preferences.IsPushEnabledAsync(userId, ct);

                    return Results.Ok(ApiResponseExtensions.Success(
                        new UpdatePreferenceResponse(userId, request.ChannelOrder, now, pushEnabled)));
                })
            .WithName("UpdateMyNotificationPreference")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<UpdatePreferenceResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Update my notification preference")
            .WithDescription("Sets the calling user's email/WhatsApp channel order and, optionally, turns push notifications on or off.")
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
