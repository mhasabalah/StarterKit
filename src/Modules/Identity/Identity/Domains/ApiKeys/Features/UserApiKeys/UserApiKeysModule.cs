using Identity.Contracts.ApiKeys;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.ApiKeys.Features.UserApiKeys;

/// <summary>Admin view/revocation of a user's API keys (<c>api-keys.manage</c>).</summary>
public class UserApiKeysModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/users/{id:guid}/api-keys")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .HasApiVersion(1.0);

        group.MapGet("", async ([FromRoute] Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetUserApiKeysQuery(id), ct))))
            .WithName("GetUserApiKeys")
            .WithSummary("List a user's API keys")
            .Produces<ApiResponse<List<ApiKeyDto>>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .RequirePermission(Permissions.ApiKeysManage);

        group.MapDelete("{keyId:guid}", async (
                [FromRoute] Guid id, [FromRoute] Guid keyId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RevokeUserApiKeyCommand(id, keyId), ct))))
            .WithName("RevokeUserApiKey")
            .WithSummary("Revoke a user's API key")
            .Produces<ApiResponse<RevokeApiKeyResult>>()
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequirePermission(Permissions.ApiKeysManage);
    }
}
