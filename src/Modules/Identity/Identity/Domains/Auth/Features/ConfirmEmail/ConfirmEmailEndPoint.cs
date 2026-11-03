using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.ConfirmEmail;

public class ConfirmEmailEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/email/confirm",
                async (ConfirmEmailCommand command, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(command, ct);
                    return Results.Ok(new ApiResponse<ConfirmEmailResult>(
                        true, StatusCodes.Status200OK, result, result.Message));
                })
            .WithName("ConfirmEmail")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .WithSummary("Confirm an email with the userId + token from the confirmation link")
            .WithDescription(
                "Anonymous: the link is the proof. After confirmation, forgot-password by email works for the " +
                "account. The email_verified claim of existing access tokens updates at the next refresh.")
            .Produces<ApiResponse<ConfirmEmailResult>>()
            .ProducesProblem(400)
            .ProducesProblem(429)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
