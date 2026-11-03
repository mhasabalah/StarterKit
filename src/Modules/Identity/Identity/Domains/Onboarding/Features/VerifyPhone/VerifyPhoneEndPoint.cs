using Identity.Contracts.Onboarding.Commands;

namespace Identity.Domains.Onboarding.Features.VerifyPhone;

public class VerifyPhoneEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/onboarding/verify-phone",
                async (VerifyOnboardingPhoneCommand command, ISender sender) =>
                {
                    var result = await sender.Send(command);
                    return Results.Ok(result);
                })
            .WithName("OnboardingVerifyPhone")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Onboarding")
            .Produces<VerifyOnboardingPhoneResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
