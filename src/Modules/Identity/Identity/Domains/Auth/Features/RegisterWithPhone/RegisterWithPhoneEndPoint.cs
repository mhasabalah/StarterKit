using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.RegisterWithPhone;

[Obsolete("Use unified /api/identity/auth/register endpoint with identifier field instead")]
public class RegisterWithPhoneEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/auth/register-phone",
            async (RegisterWithPhoneCommand command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("RegisterWithPhone")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<RegisterResult>(200)
            .ProducesProblem(400)
            .WithOpenApi(operation =>
            {
                operation.Deprecated = true;
                operation.Description = "⚠️ DEPRECATED: Use POST /api/identity/auth/register with 'identifier' field instead";
                return operation;
            })
            .HasApiVersion(1.0);
    }
}
