using Identity.Contracts.Auth.Commands;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.VerifyPhone;

public class VerifyPhoneEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/phone/verify",
                async (VerifyPhoneRequest request, ISender sender, IHttpContextService httpContextService,
                    CancellationToken ct) =>
                {
                    // The user is ALWAYS the caller (claims), never a body-supplied id.
                    var userId = httpContextService.GetUserIdAsGuid();
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await sender.Send(new VerifyPhoneCommand(userId.Value, request.VerificationCode), ct);
                    return Results.Ok(result);
                })
            .WithName("VerifyPhone")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Phone Verification")
            .WithSummary("Verify the signed-in user's phone with the code that was sent")
            .Produces<VerifyPhoneResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}

public record VerifyPhoneRequest(string VerificationCode);
