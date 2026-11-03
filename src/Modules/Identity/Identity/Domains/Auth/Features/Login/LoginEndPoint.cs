using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.Login;

public class LoginEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/login", async (LoginCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            
            return Results.Ok(new ApiResponse<LoginResult>(
                true,
                StatusCodes.Status200OK,
                result,
                "Login successful"));
        })
        .WithName("Login")
        .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
        .WithTags("Authentication")
        .Produces<ApiResponse<LoginResult>>()
        .WithSummary("Login user")
        .WithDescription("Authenticate user with email and password, returns JWT token")
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .HasApiVersion(1.0);
    }
}
