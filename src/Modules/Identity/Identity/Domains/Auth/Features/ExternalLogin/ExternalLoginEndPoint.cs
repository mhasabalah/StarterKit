using Identity.Contracts.Auth.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;

namespace Identity.Domains.Auth.Features.ExternalLogin;

public class ExternalLoginEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Initiate external login
        app.MapGet("/api/identity/auth/external-login",
            async ([AsParameters] ExternalLoginRequest request, [FromServices] ISender sender) =>
            {
                var command = new ExternalLoginCommand(request.Provider, request.ReturnUrl);
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("ExternalLogin")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ExternalLoginResult>(200)
            .WithOpenApi()
            .HasApiVersion(1.0);

        // Challenge endpoint that triggers the external authentication
        app.MapGet("/api/identity/auth/challenge/{provider}",
            (string provider, string? returnUrl, HttpContext context) =>
            {
                var redirectUrl = $"/api/identity/auth/external-callback?returnUrl={returnUrl}";
                var properties = new AuthenticationProperties 
                { 
                    RedirectUri = redirectUrl 
                };
                return Results.Challenge(properties, [provider]);
            })
            .WithName("ExternalLoginChallenge")
            .WithTags("Authentication")
            .ExcludeFromDescription();

        // Callback from external provider
        app.MapGet("/api/identity/auth/external-callback",
            async ([AsParameters] ExternalCallbackRequest request, [FromServices] ISender sender) =>
            {
                var command = new ExternalLoginCallbackCommand(request.RemoteError);
                var result = await sender.Send(command);
                return Results.Ok(result);
            })
            .WithName("ExternalLoginCallback")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ExternalLoginCallbackResult>(200)
            .ProducesProblem(400)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}

public record ExternalLoginRequest(string Provider, string ReturnUrl);
public record ExternalCallbackRequest(string? RemoteError);
