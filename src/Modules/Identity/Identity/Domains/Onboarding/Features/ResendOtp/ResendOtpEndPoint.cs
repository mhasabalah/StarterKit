using Identity.Contracts.Onboarding.Commands;

namespace Identity.Domains.Onboarding.Features.ResendOtp;

public class ResendOtpEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/onboarding/resend-otp",
                async (ResendOnboardingOtpCommand command, ISender sender) =>
                {
                    var result = await sender.Send(command);
                    return Results.Ok(result);
                })
            .WithName("OnboardingResendOtp")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Onboarding")
            .Produces<ResendOnboardingOtpResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .WithOpenApi()
            .AllowAnonymous()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
