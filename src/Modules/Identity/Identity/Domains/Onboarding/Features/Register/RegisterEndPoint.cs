using Identity.Contracts.Onboarding.Commands;

namespace Identity.Domains.Onboarding.Features.Register;

public class RegisterEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/onboarding/register",
                async (RegisterOnboardingCommand command, ISender sender) =>
                {
                    var result = await sender.Send(command);
                    return Results.Ok(result);
                })
            .WithName("OnboardingRegister")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Onboarding")
            .Produces<RegisterOnboardingResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .AllowAnonymous()
            .RequireRateLimiting("otp")
            .HasApiVersion(1.0);
    }
}
