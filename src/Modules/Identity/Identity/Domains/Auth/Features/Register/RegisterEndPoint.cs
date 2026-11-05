using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.Register;

public class RegisterEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/register", async (RegisterCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);

            return Results.Ok(new ApiResponse<RegisterResult>(
                true,
                StatusCodes.Status200OK,
                result,
                result.Message));
        })
        .WithName("Register")
        .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
        .WithTags("Authentication")
        .Produces<ApiResponse<RegisterResult>>()
        .WithSummary("Register new user")
        .WithDescription("Register a new user with email and password")
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .HasApiVersion(1.0);
    }
}
