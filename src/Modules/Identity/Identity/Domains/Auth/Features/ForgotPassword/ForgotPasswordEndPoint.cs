using Shared.Core.BotProtection;
using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.ForgotPassword;

public class ForgotPasswordEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/forgot-password",
            async (ForgotPasswordCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(new ApiResponse<ForgotPasswordResult>(
                    true, StatusCodes.Status200OK, result, result.Message));
            })
            .WithName("ForgotPassword")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ApiResponse<ForgotPasswordResult>>()
            .ProducesProblem(400)
            .WithSummary("Request password reset")
            .WithDescription(
                "Accepts an email or phone number. " +
                "Email accounts receive a reset link; phone accounts receive a 6-digit OTP via SMS. " +
                "Always returns 200 regardless of whether the account exists.")
            .AllowAnonymous()
            .RequireRateLimiting("otp")
            .RequireHumanVerification("forgot-password")
            .HasApiVersion(1.0);
    }
}
