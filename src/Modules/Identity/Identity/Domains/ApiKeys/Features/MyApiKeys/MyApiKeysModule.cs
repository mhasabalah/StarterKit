using Identity.Contracts.ApiKeys;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Extensions;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.ApiKeys.Features.MyApiKeys;

public sealed record CreateApiKeyRequest(string Name, List<string> Scopes, int? ExpiresInDays = null);

/// <summary>Self-service API keys. Management is only possible with an interactive session (never with a key).</summary>
public class MyApiKeysModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/me/api-keys")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("API Keys")
            .HasApiVersion(1.0);

        group.MapPost("", async ([FromBody] CreateApiKeyRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new CreateApiKeyCommand(request.Name, request.Scopes, request.ExpiresInDays), ct);
                return Results.Ok(ApiResponse.Success(result, "Copy the key now: it is shown only once."));
            })
            .WithName("CreateApiKey")
            .WithSummary("Create an API key")
            .WithDescription("Returns the key (sk_<prefix>_<secret>) exactly once; only its SHA-256 hash is stored. " +
                             "Send it as 'Authorization: ApiKey <key>' or 'X-Api-Key: <key>'. The key's permissions are " +
                             "the owner's permissions limited to the requested scopes.")
            .Produces<ApiResponse<CreateApiKeyResult>>()
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireRateLimiting(RateLimitPolicyNames.Create)
            .RequireAuthorization();

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetMyApiKeysQuery(), ct))))
            .WithName("GetMyApiKeys")
            .WithSummary("List my API keys")
            .Produces<ApiResponse<List<ApiKeyDto>>>()
            .DisallowApiKey()
            .RequireAuthorization();

        group.MapDelete("{id:guid}", async ([FromRoute] Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RevokeMyApiKeyCommand(id), ct))))
            .WithName("RevokeMyApiKey")
            .WithSummary("Revoke one of my API keys")
            .Produces<ApiResponse<RevokeApiKeyResult>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization();
    }
}
