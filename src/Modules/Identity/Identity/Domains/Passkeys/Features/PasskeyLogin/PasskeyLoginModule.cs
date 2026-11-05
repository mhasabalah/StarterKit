using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Passkeys;

namespace Identity.Domains.Passkeys.Features.PasskeyLogin;

/// <summary>
/// Passkey sign-in (anonymous). No CAPTCHA: a WebAuthn assertion cannot be scripted without the private key and
/// the ceremony is single-use, so the "auth" rate limit is enough.
/// </summary>
public class PasskeyLoginModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("api/auth/passkeys/assertion")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Passkeys")
            .HasApiVersion(1.0);

        group.MapPost("options", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new BeginPasskeyLoginCommand(), ct))))
            .WithName("BeginPasskeyLogin")
            .WithSummary("Start a passkey sign-in")
            .WithDescription(
                "Usernameless WebAuthn request options for navigator.credentials.get() (works with conditional " +
                "mediation / passkey autofill) and a single-use ceremonyId.")
            .Produces<ApiResponse<PasskeyOptionsResult>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("", async (CompletePasskeyLoginRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new CompletePasskeyLoginCommand(request.CeremonyId, request.Credential.GetRawText()), ct);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .WithName("CompletePasskeyLogin")
            .WithSummary("Finish a passkey sign-in")
            .WithDescription("Verifies the assertion (credential.toJSON()) and returns an MFA session, like /api/auth/login.")
            .Produces<ApiResponse<LoginResult>>()
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
    }
}

/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as returned by <c>credential.toJSON()</c>.</param>
public record CompletePasskeyLoginRequest(string CeremonyId, System.Text.Json.JsonElement Credential);
