using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.VerifyResetOtp;

public class VerifyResetOtpEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/verify-reset-otp",
            async (VerifyResetOtpCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(new ApiResponse<VerifyResetOtpResult>(
                    true, StatusCodes.Status200OK, result, result.Message));
            })
            .WithName("VerifyResetOtp")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ApiResponse<VerifyResetOtpResult>>()
            .ProducesProblem(400)
            .WithSummary("Verify phone OTP for password reset")
            .WithDescription(
                "Validates the 6-digit OTP sent via SMS. " +
                "On success, returns a userId and resetToken to use with POST /api/auth/reset-password. " +
                "Allows up to 3 wrong attempts before the OTP is invalidated.")
            .WithOpenApi()
            .AllowAnonymous()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
