namespace Shared.Notification.Endpoints;

public sealed record SendVerificationCodeRequest(
    VerificationPurpose Purpose,
    NotificationCulture Culture = NotificationCulture.En);

public sealed record SendVerificationCodeResponse(string Channel);

public sealed class SendVerificationCodeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/verification/send",
                async (
                    SendVerificationCodeRequest req,
                    IVerificationService verificationService,
                    IHttpContextService httpCtx,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var result = await verificationService.SendCodeAsync(
                        userId, req.Purpose, req.Culture, ct);

                    if (!result.Success)
                        return ApiResponseExtensions.Error(
                            result.Error ?? "Failed to send verification code.", 400);

                    return Results.Ok(ApiResponseExtensions.Success(
                        new SendVerificationCodeResponse(result.Channel.ToString())));
                })
            .WithName("SendVerificationCode")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<SendVerificationCodeResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Send a verification code")
            .WithDescription("Generates and delivers a 6-digit OTP to the calling user via the channel determined by the requested purpose.")
            .WithOpenApi()
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
