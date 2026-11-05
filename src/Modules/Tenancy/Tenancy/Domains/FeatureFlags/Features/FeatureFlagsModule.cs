using Tenancy.Contracts.FeatureFlags;

namespace Tenancy.Domains.FeatureFlags.Features;

public record UpdateFeatureFlagRequest(string? Description, bool Enabled, int RolloutPercent, bool KillSwitch, long? Version = null);

public record SetFeatureFlagOverrideRequest(Guid? TenantId, Guid? UserId, bool Enabled);

/// <summary>Runtime feature flags: platform administration (SuperAdmin) and the caller's own evaluated view.</summary>
public class FeatureFlagsModule : ICarterModule
{
    private const string Tag = "Feature Flags";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Tenancy").Build();

        // ----- platform admin (never inside a tenant) -----
        var admin = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/feature-flags")
            .WithTags(Tag).WithApiVersionSet(versionSet).HasApiVersion(1.0).HostOnly();

        admin.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetFeatureFlagsQuery(), ct))))
            .WithName("GetFeatureFlags").Produces<ApiResponse<IReadOnlyList<FeatureFlagDto>>>()
            .WithSummary("List feature flags")
            .WithDescription("Every stored flag with its tenant and user overrides. Permission feature-flags.manage.")
            .RequirePermission(Permissions.FeatureFlagsManage);

        admin.MapGet("/{key}", async (string key, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetFeatureFlagByKeyQuery(key), ct))))
            .WithName("GetFeatureFlagByKey").Produces<ApiResponse<FeatureFlagDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get feature flag")
            .RequirePermission(Permissions.FeatureFlagsManage);

        admin.MapPost("/", async (CreateFeatureFlagCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"{EndpointConfig.BaseApiPath}/admin/feature-flags/{result.Key}",
                    ApiResponseExtensions.Success(result));
            })
            .WithName("CreateFeatureFlag").Produces<ApiResponse<CreateFeatureFlagResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create feature flag")
            .WithDescription("Key: lowercase letters, digits, '.', '_' or '-'. RolloutPercent 0 = use Enabled, 1-99 = share of tenants, 100 = all.")
            .RequirePermission(Permissions.FeatureFlagsManage)
            .DisallowImpersonation();

        admin.MapPut("/{key}", async (string key, UpdateFeatureFlagRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new UpdateFeatureFlagCommand(key,
                    request.Description, request.Enabled, request.RolloutPercent, request.KillSwitch, request.Version), ct))))
            .WithName("UpdateFeatureFlag").Produces<ApiResponse<UpdateFeatureFlagResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update feature flag")
            .WithDescription("Toggle, change the rollout percentage or pull the kill switch. Send `version` to detect concurrent edits.")
            .RequirePermission(Permissions.FeatureFlagsManage)
            .DisallowImpersonation();

        admin.MapDelete("/{key}", async (string key, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteFeatureFlagCommand(key), ct))))
            .WithName("DeleteFeatureFlag").Produces<ApiResponse<DeleteFeatureFlagResult>>()
            .WithSummary("Delete feature flag")
            .WithDescription("Removes the flag and its overrides; the flag then falls back to FeatureFlags:Defaults.")
            .RequirePermission(Permissions.FeatureFlagsManage)
            .DisallowImpersonation();

        admin.MapPut("/{key}/overrides", async (string key, SetFeatureFlagOverrideRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new SetFeatureFlagOverrideCommand(key, request.TenantId, request.UserId, request.Enabled), ct))))
            .WithName("SetFeatureFlagOverride").Produces<ApiResponse<SetFeatureFlagOverrideResult>>()
            .WithSummary("Set a tenant or user override")
            .WithDescription("Exactly one of tenantId / userId. A user override beats a tenant override; the kill switch beats both.")
            .RequirePermission(Permissions.FeatureFlagsManage)
            .DisallowImpersonation();

        admin.MapDelete("/{key}/overrides/{overrideId:guid}", async (string key, Guid overrideId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RemoveFeatureFlagOverrideCommand(key, overrideId), ct))))
            .WithName("RemoveFeatureFlagOverride").Produces<ApiResponse<RemoveFeatureFlagOverrideResult>>()
            .WithSummary("Remove an override")
            .RequirePermission(Permissions.FeatureFlagsManage)
            .DisallowImpersonation();

        // ----- the caller's own view (any authenticated user; tenant optional) -----
        app.MapGet(EndpointConfig.BaseApiPath + "/me/feature-flags",
                async (ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetMyFeatureFlagsQuery(), ct))))
            .WithName("GetMyFeatureFlags").WithTags(Tag).WithApiVersionSet(versionSet).HasApiVersion(1.0)
            .Produces<ApiResponse<IReadOnlyDictionary<string, bool>>>()
            .WithSummary("My feature flags")
            .WithDescription("Every flag evaluated for the current user and tenant (`{ \"key\": true|false }`), for toggling UI.")
            .RequireAuthorization();
    }
}
