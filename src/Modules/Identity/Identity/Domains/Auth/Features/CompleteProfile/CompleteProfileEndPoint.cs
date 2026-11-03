using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.CompleteProfile;

public class CompleteProfileEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/identity/profile/complete",
            async (CompleteProfileCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("CompleteProfile")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Profile")
            .RequireAuthorization()
            .Produces<CompleteProfileResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .HasApiVersion(1.0);
    }
}
