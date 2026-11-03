using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.VerifyTwoFactor;

public class VerifyTwoFactorEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/2fa/verify", async (VerifyTwoFactorCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(new ApiResponse<LoginResult>(true, StatusCodes.Status200OK, result, result.Message));
            })
            .WithName("VerifyTwoFactor")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Two-Factor Authentication")
            .WithSummary("Complete a login with the second factor")
            .WithDescription(
                "Send the twoFactorChallengeToken from the login response plus an authenticator code (or a " +
                "recovery code). Returns the session; the access token carries amr=mfa. Wrong codes count towards " +
                "account lockout.")
            .Produces<ApiResponse<LoginResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
