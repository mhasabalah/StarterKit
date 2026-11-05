using Identity.Contracts.Passkeys;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.Passkeys.Features.MyPasskeys;

/// <summary>
/// Self-service passkey management. Every route is the caller's own account, refuses API keys (credentials are
/// managed by the human) and impersonated sessions (support never changes how a customer signs in).
/// </summary>
public class MyPasskeysModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("api/auth/passkeys")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Passkeys")
            .HasApiVersion(1.0);

        group.MapPost("registration/options", async (ISender sender, IHttpContextService http, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(
                    await sender.Send(new BeginPasskeyRegistrationCommand(CallerId(http)), ct))))
            .WithName("BeginPasskeyRegistration")
            .WithSummary("Start adding a passkey")
            .WithDescription(
                "Returns WebAuthn creation options for navigator.credentials.create() and a single-use ceremonyId. " +
                "Step-up: the session must have signed in within Identity:Passkeys:FreshAuthMinutes, otherwise " +
                "403 reauthentication_required.")
            .Produces<ApiResponse<PasskeyOptionsResult>>()
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization()
            .RequireRateLimiting("auth");

        group.MapPost("registration", async (
                CompletePasskeyRegistrationRequest request, ISender sender, IHttpContextService http, CancellationToken ct) =>
            {
                var result = await sender.Send(new CompletePasskeyRegistrationCommand(
                    CallerId(http), request.CeremonyId, request.Credential.GetRawText(), request.Name), ct);
                return Results.Ok(ApiResponse.Success(result, "Passkey added"));
            })
            .WithName("CompletePasskeyRegistration")
            .WithSummary("Finish adding a passkey")
            .WithDescription("Verifies the attestation (credential.toJSON()) and stores the passkey. Sends a security email.")
            .Produces<ApiResponse<PasskeyDto>>()
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization()
            .RequireRateLimiting("auth");

        group.MapGet("", async (ISender sender, IHttpContextService http, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetMyPasskeysQuery(CallerId(http)), ct))))
            .WithName("GetMyPasskeys")
            .WithSummary("List my passkeys")
            .Produces<ApiResponse<List<PasskeyDto>>>()
            .DisallowApiKey()
            .RequireAuthorization();

        group.MapPatch("{id}", async (
                [FromRoute] string id, RenamePasskeyRequest request, ISender sender, IHttpContextService http,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(
                    await sender.Send(new RenamePasskeyCommand(CallerId(http), id, request.Name), ct))))
            .WithName("RenamePasskey")
            .WithSummary("Rename one of my passkeys")
            .Produces<ApiResponse<PasskeyDto>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization();

        group.MapDelete("{id}", async ([FromRoute] string id, ISender sender, IHttpContextService http, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new DeletePasskeyCommand(CallerId(http), id), ct))))
            .WithName("DeletePasskey")
            .WithSummary("Remove one of my passkeys")
            .WithDescription("409 last_sign_in_method when it is the account's only way to sign in.")
            .Produces<ApiResponse<DeletePasskeyResult>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization();
    }

    private static Guid CallerId(IHttpContextService http) =>
        http.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");
}

/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as returned by <c>credential.toJSON()</c>.</param>
public record CompletePasskeyRegistrationRequest(string CeremonyId, System.Text.Json.JsonElement Credential, string? Name);

public record RenamePasskeyRequest(string Name);
