using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.SendEmailConfirmation;

public class SendEmailConfirmationEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/email/send-confirmation",
                async (ISender sender, IHttpContextService httpContextService, CancellationToken ct) =>
                {
                    // Always the caller: a body-supplied user id would let anyone spam other accounts.
                    var userId = httpContextService.GetUserIdAsGuid();
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await sender.Send(new SendEmailConfirmationCommand(userId.Value), ct);
                    return Results.Ok(new ApiResponse<SendEmailConfirmationResult>(
                        true, StatusCodes.Status200OK, result, result.Message));
                })
            .WithName("SendEmailConfirmation")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .WithSummary("Send (or resend) the email confirmation link to the signed-in user")
            .WithDescription(
                "Sends a link to {EmailConfirmation:FrontendBaseUrl}/confirm-email?userId=..&token=.. . " +
                "The frontend posts both values to POST /api/auth/email/confirm. Links expire after " +
                "EmailConfirmation:TokenExpiryHours and are invalidated by an email or password change.")
            .Produces<ApiResponse<SendEmailConfirmationResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .RequireAuthorization()
            .RequireRateLimiting("otp")
            .HasApiVersion(1.0);
    }
}
