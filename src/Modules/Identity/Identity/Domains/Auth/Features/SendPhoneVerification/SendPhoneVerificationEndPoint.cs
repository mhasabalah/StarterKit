using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.SendPhoneVerification;

public class SendPhoneVerificationEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/phone/send-verification",
            async (SendPhoneVerificationCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("SendPhoneVerification")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Phone Verification")
            .Produces<SendPhoneVerificationResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
