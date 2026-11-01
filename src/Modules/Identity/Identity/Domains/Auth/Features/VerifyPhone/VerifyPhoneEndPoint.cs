using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.VerifyPhone;

public class VerifyPhoneEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/phone/verify",
            async (VerifyPhoneCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("VerifyPhone")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Phone Verification")
            .Produces<VerifyPhoneResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
