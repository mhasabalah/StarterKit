using Identity.Services.Otp;
using Shared.Core.Web;

namespace Identity.Domains.Verification.Features.ValidateVerificationCode;

public sealed record ValidateVerificationCodeRequest(
    VerificationPurpose Purpose,
    string Code);

/// <summary>Route and shapes unchanged from the former Notification endpoint.</summary>
public sealed class ValidateVerificationCodeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/verification/validate",
                async (
                    ValidateVerificationCodeRequest req,
                    ISender sender,
                    IHttpContextService httpCtx,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var result = await sender.Send(
                        new ValidateVerificationCodeCommand(userId, req.Purpose, req.Code ?? string.Empty), ct);

                    return result.Status switch
                    {
                        OtpVerificationStatus.Success =>
                            Results.Ok(ApiResponseExtensions.Success<object?>(null, "Code verified successfully.")),

                        OtpVerificationStatus.Expired =>
                            ApiResponseExtensions.Error("Verification code has expired. Please request a new one.", 400),

                        OtpVerificationStatus.Locked =>
                            ApiResponseExtensions.Error("Too many failed attempts. Please request a new code.", 400),

                        // Mismatch and NotFound (incl. already used) share one message on purpose (security).
                        _ => ApiResponseExtensions.Error("Invalid verification code.", 400)
                    };
                })
            .WithName("ValidateVerificationCode")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Validate a verification code")
            .WithDescription("Validates the one-time code submitted by the calling user.")
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
