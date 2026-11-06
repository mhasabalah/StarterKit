namespace Notification.Features.Preferences;

/// <param name="ChannelOrder">Email/WhatsApp fallback order (in-app and push are not part of it).</param>
/// <param name="PushEnabled">Turn push notifications on/off; omit to keep the current setting.</param>
/// <param name="CategoryMutes">
/// Muted channels per category group (<c>billing</c>, <c>membership</c>, ...; <c>security</c> cannot be muted). REPLACES
/// the whole map when given; omit to keep it.
/// </param>
/// <param name="QuietHours">Daily window with no live push (push is deferred to its end); <c>Enabled=false</c> removes it; omit to keep it.</param>
/// <param name="TimeZone">IANA time zone id the quiet window is read in (e.g. <c>Africa/Cairo</c>); required with the first window.</param>
public sealed record UpdatePreferenceRequest(
    NotificationChannel[] ChannelOrder,
    bool? PushEnabled = null,
    Dictionary<string, ChannelMuteSet>? CategoryMutes = null,
    QuietHoursDto? QuietHours = null,
    string? TimeZone = null);

public sealed record UpdatePreferenceResponse(
    Guid UserId,
    NotificationChannel[] ChannelOrder,
    DateTime UpdatedAt,
    bool PushEnabled,
    IReadOnlyDictionary<string, ChannelMuteSet> CategoryMutes,
    QuietHoursDto? QuietHours,
    string? TimeZone);

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
                    IStringLocalizer<NotificationResource> localizer,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    return await PreferenceRules.ApplyAsync(userId, request, preferences, adapters, localizer, ct);
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
