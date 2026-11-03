using System.Globalization;
using Identity.Services.Otp;
using Shared.Core.Web;

namespace Identity.Domains.Verification.Features.SendVerificationCode;

public sealed record SendVerificationCodeRequest(
    VerificationPurpose Purpose,
    NotificationCulture Culture = NotificationCulture.En);

public sealed record SendVerificationCodeResponse(string Channel);

/// <summary>
/// Route and shapes unchanged from the former Notification endpoint; the code is now issued by Identity's
/// unified OTP service and only delivered by Notification.
/// </summary>
public sealed class SendVerificationCodeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/verification/send",
                async (
                    SendVerificationCodeRequest req,
                    ISender sender,
                    IHttpContextService httpCtx,
                    HttpContext http,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var result = await sender.Send(
                        new SendVerificationCodeCommand(userId, req.Purpose, req.Culture), ct);

                    if (result.IsRateLimited)
                    {
                        if (result.RetryAfter is { } retryAfter)
                            http.Response.Headers.RetryAfter =
                                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                        return ApiResponseExtensions.Error(
                            result.Error ?? "Too many verification code requests.",
                            StatusCodes.Status429TooManyRequests);
                    }

                    if (!result.Success)
                    {
                        return ApiResponseExtensions.Error(
                            result.Error ?? "Failed to send verification code.", 400);
                    }

                    return Results.Ok(ApiResponseExtensions.Success(
                        new SendVerificationCodeResponse(result.Channel!.Value.ToString())));
                })
            .WithName("SendVerificationCode")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<SendVerificationCodeResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)
            .WithSummary("Send a verification code")
            .WithDescription("Generates and delivers a one-time code to the calling user via the channel determined by the requested purpose.")
            .RequireAuthorization()
            .RequireRateLimiting("otp")
            .HasApiVersion(1.0);
    }
}
