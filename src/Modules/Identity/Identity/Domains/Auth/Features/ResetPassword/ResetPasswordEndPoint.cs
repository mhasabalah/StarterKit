using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.ResetPassword;

public class ResetPasswordEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/reset-password",
            async (ResetPasswordCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(new ApiResponse<ResetPasswordResult>(
                    true, StatusCodes.Status200OK, result, result.Message));
            })
            .WithName("ResetPassword")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ApiResponse<ResetPasswordResult>>()
            .ProducesProblem(400)
            .WithSummary("Reset password")
            .WithDescription(
                "Sets a new password using email + token from the set-password/reset link, " +
                "or userId + token from POST /api/auth/verify-reset-otp. " +
                "The token is single-use and expires after PasswordReset:TokenExpiryHours. " +
                "All existing sessions of the account are signed out.")
            .AllowAnonymous()
            .RequireRateLimiting("auth")
        .HasApiVersion(1.0);
    }
}
