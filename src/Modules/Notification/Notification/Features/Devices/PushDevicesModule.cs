using Notification.Contracts.Devices;

namespace Notification.Features.Devices;

/// <summary>
/// The caller's push devices. A person's own app registers here after sign-in, so API keys are refused
/// (<c>DisallowApiKey</c>): an integration key must not be able to redirect a user's notifications to another device.
/// </summary>
public sealed class PushDevicesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/notifications/devices")
            .WithTags("Notifications")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .HasApiVersion(1.0);

        group.MapPut("/", async (RegisterPushDeviceRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new RegisterPushDeviceCommand(request.Token, request.Platform, request.AppVersion, request.Locale), ct))))
            .WithName("RegisterPushDevice")
            .Produces<ApiResponse<RegisterPushDeviceResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Register my device")
            .WithDescription("Upsert by FCM token. Call it after sign-in, on every app start and when FCM rotates the token. " +
                             "The device is bound to the current session: signing out (or revoking the session) stops its pushes.")
            .RequireAuthorization()
            .DisallowApiKey()
            .RequireRateLimiting("create");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetMyPushDevicesQuery(), ct))))
            .WithName("GetMyPushDevices")
            .Produces<ApiResponse<List<PushDeviceDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("My devices")
            .WithDescription("The caller's registered push devices (token masked).")
            .RequireAuthorization()
            .DisallowApiKey()
            .RequireRateLimiting("search");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new UnregisterPushDeviceCommand(id), ct))))
            .WithName("UnregisterPushDevice")
            .Produces<ApiResponse<UnregisterPushDeviceResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Unregister a device")
            .WithDescription("Call it on sign-out (before the token is discarded). A foreign or unknown id is 404.")
            .RequireAuthorization()
            .DisallowApiKey()
            .RequireRateLimiting("create");
    }
}
