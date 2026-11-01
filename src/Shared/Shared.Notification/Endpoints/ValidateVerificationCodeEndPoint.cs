namespace Shared.Notification.Endpoints;

public sealed record ValidateVerificationCodeRequest(
    VerificationPurpose Purpose,
    string Code);

public sealed class ValidateVerificationCodeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/verification/validate",
                async (
                    ValidateVerificationCodeRequest req,
                    IVerificationService verificationService,
                    IHttpContextService httpCtx,
                    CancellationToken ct) =>
                {
                    var userId = httpCtx.GetUserIdAsGuid()
                                 ?? throw new UnauthorizedAccessException("User id is missing.");

                    var result = await verificationService.ValidateCodeAsync(
                        userId, req.Code, req.Purpose, ct);

                    return result switch
                    {
                        VerificationResult.Success =>
                            Results.Ok(ApiResponseExtensions.Success<object?>(null, "Code verified successfully.")),

                        // Mismatch and NotFound share the same error message on purpose (security)
                        VerificationResult.Mismatch or VerificationResult.NotFound =>
                            ApiResponseExtensions.Error("Invalid verification code.", 400),

                        VerificationResult.Expired =>
                            ApiResponseExtensions.Error("Verification code has expired. Please request a new one.", 400),

                        VerificationResult.AlreadyUsed =>
                            ApiResponseExtensions.Error("Verification code has already been used.", 400),

                        VerificationResult.MaxAttemptsReached =>
                            ApiResponseExtensions.Error("Too many failed attempts. Please request a new code.", 400),

                        _ => Results.StatusCode(500)
                    };
                })
            .WithName("ValidateVerificationCode")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Validate a verification code")
            .WithDescription("Validates the 6-digit OTP submitted by the calling user.")
            .WithOpenApi()
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
