using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.SendPhoneVerification;

public class SendPhoneVerificationEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/phone/send-verification",
                async (ISender sender, IHttpContextService httpContextService, CancellationToken ct) =>
                {
                    // The user is ALWAYS the caller; a body-supplied user id would allow attacking other accounts.
                    var userId = httpContextService.GetUserIdAsGuid();
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await sender.Send(new SendPhoneVerificationCommand(userId.Value), ct);
                    return Results.Ok(result);
                })
            .WithName("SendPhoneVerification")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Phone Verification")
            .WithSummary("Send a verification code to the signed-in user's phone")
            .Produces<SendPhoneVerificationResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .RequireAuthorization()
            .RequireRateLimiting("otp")
            .HasApiVersion(1.0);
    }
}
