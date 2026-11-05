namespace Notification.Features.Preferences;

public sealed class UpdateUserPreferenceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/notifications/preferences/{userId:guid}",
                async (
                    Guid userId,
                    UpdatePreferenceRequest request,
                    INotificationPreferenceRepository preferences,
                    IEnumerable<INotificationChannelAdapter> adapters,
                    CancellationToken ct) =>
                {
                    var validationError = ChannelOrderRules.Validate(request.ChannelOrder, adapters);
                    if (validationError is not null)
                        return ApiResponseExtensions.BadRequest(validationError);

                    var now = await preferences.UpsertAsync(userId, request.ChannelOrder, request.PushEnabled, ct);
                    var pushEnabled = request.PushEnabled ?? await preferences.IsPushEnabledAsync(userId, ct);

                    return Results.Ok(ApiResponseExtensions.Success(
                        new UpdatePreferenceResponse(userId, request.ChannelOrder, now, pushEnabled)));
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
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
