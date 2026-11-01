using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;

namespace Identity.Domains.Users.Features.GetMyProfile;

public class GetMyProfileEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/profile",
                async (ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetMyProfileQuery(), cancellationToken);
                    return Results.Ok(ApiResponse.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetMyProfile")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Profile")
            .WithSummary("Get my profile")
            .WithDescription("Retrieve the authenticated user's profile information.")
            .Produces<ApiResponse<MyProfileDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
