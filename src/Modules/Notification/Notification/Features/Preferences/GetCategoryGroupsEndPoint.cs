using Notification.Contracts.Preferences;

namespace Notification.Features.Preferences;

/// <summary>Lookup of the notification category groups a user can mute (bilingual names, <c>mutable=false</c> for security).</summary>
public sealed class GetCategoryGroupsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/notifications/preferences/category-groups",
                () => Results.Ok(ApiResponseExtensions.Success(NotificationCategoryGroups.All)))
            .WithName("GetNotificationCategoryGroups")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<IReadOnlyList<NotificationCategoryGroup>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("List notification category groups")
            .WithDescription("The groups a user can mute per channel (billing, membership, ...), with localized names. 'security' is listed with mutable=false.")
            .RequireAuthorization()
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
