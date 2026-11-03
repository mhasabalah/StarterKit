using Microsoft.AspNetCore.Mvc;

namespace Tenancy.Domains.Tenants.Features;

public record TransferOwnershipRequest(Guid NewOwnerUserId);

public record TenantReasonRequest(string? Reason);

public record ChangeIsolationRequest(TenantIsolation Isolation, string? ConnectionSecretRef);

public record AcceptInvitationRequest(string Token);

/// <summary>
/// Tenant routes:
/// <list type="bullet">
/// <item><c>/tenants</c> (create) and <c>/me/tenants</c>: no tenant needed (sign-up, tenant switcher).</item>
/// <item><c>/tenants/current/*</c>: the current tenant (resolved by token / host / header).</item>
/// <item><c>/host/tenants/*</c>: platform administration (platform permissions, host scope).</item>
/// </list>
/// </summary>
public class TenantsModule : ICarterModule
{
    private const string Tag = "Tenants";
    private const string HostTag = "Host - Tenants";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Tenancy").Build();
        var root = EndpointConfig.BaseApiPath;

        // ----- no tenant -----
        var account = app.MapGroup(root).WithTags(Tag).WithApiVersionSet(versionSet).HasApiVersion(1.0)
            .HostOnly().RequireAuthorization();

        account.MapPost("/tenants", async (CreateTenantCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"api/v1/tenants/{result.TenantId}", ApiResponseExtensions.Success(result));
            })
            .WithName("CreateTenant")
            .Produces<ApiResponse<CreateTenantResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create tenant")
            .WithDescription("Self-service sign-up: creates a company owned by the caller (shared isolation). " +
                             "Platform admins (tenants.manage) may choose dedicated isolation and the owner.")
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        account.MapGet("/me/tenants", async (IHttpContextService http, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new GetUserTenantsQuery(http.GetUserIdAsGuid() ?? throw new UnauthorizedException("Authentication is required.")), ct))))
            .WithName("GetMyTenants")
            .Produces<ApiResponse<IReadOnlyList<UserTenantDto>>>()
            .WithSummary("My tenants")
            .WithDescription("Tenants the caller belongs to (tenant switcher). Switch with POST /auth/switch-tenant.");

        account.MapPost("/tenant-invitations/accept", async (AcceptInvitationRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new AcceptInvitationCommand(request.Token), ct))))
            .WithName("AcceptTenantInvitation")
            .Produces<ApiResponse<AcceptInvitationResult>>()
            .WithSummary("Accept invitation")
            .WithDescription("Joins the inviting tenant (the signed-in user's verified e-mail must match). " +
                             "Then switch to it with POST /auth/switch-tenant.")
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        // ----- current tenant -----
        var current = app.MapGroup(root + "/tenants/current").WithTags(Tag).WithApiVersionSet(versionSet)
            .HasApiVersion(1.0).RequireTenant();

        current.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetCurrentTenantQuery(), ct))))
            .WithName("GetCurrentTenant").Produces<ApiResponse<TenantDto>>()
            .WithSummary("Current tenant").WithDescription("Profile of the current tenant. Members only.")
            .RequireTenantMember();

        current.MapPut("/", async (UpdateTenantSettingsCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(command, ct))))
            .WithName("UpdateTenantSettings").Produces<ApiResponse<UpdateTenantSettingsResult>>()
            .WithSummary("Update tenant settings").WithDescription("Name, legal name, locale, currency, branding.")
            .RequireTenantPermission(TenantPermissions.SettingsManage);

        current.MapGet("/domains", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTenantDomainsQuery(), ct))))
            .WithName("GetTenantDomains").Produces<ApiResponse<IReadOnlyList<TenantDomainDto>>>()
            .WithSummary("Custom domains")
            .RequireTenantPermission(TenantPermissions.SettingsManage);

        current.MapPost("/domains", async (AddTenantDomainCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(command, ct))))
            .WithName("AddTenantDomain").Produces<ApiResponse<TenantDomainDto>>()
            .WithSummary("Add custom domain")
            .WithDescription("Returns the DNS TXT record to create; then call verify.")
            .RequireTenantPermission(TenantPermissions.SettingsManage);

        current.MapPost("/domains/{host}/verify", async (string host, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new VerifyTenantDomainCommand(host), ct))))
            .WithName("VerifyTenantDomain").Produces<ApiResponse<TenantDomainDto>>()
            .WithSummary("Verify custom domain")
            .RequireTenantPermission(TenantPermissions.SettingsManage)
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        current.MapDelete("/domains/{host}", async (string host, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RemoveTenantDomainCommand(host), ct))))
            .WithName("RemoveTenantDomain").Produces<ApiResponse<RemoveTenantDomainResult>>()
            .WithSummary("Remove custom domain")
            .RequireTenantPermission(TenantPermissions.SettingsManage);

        current.MapPost("/transfer-ownership", async (TransferOwnershipRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new TransferTenantOwnershipCommand(request.NewOwnerUserId), ct))))
            .WithName("TransferTenantOwnership").Produces<ApiResponse<TransferTenantOwnershipResult>>()
            .WithSummary("Transfer ownership").WithDescription("Owner only.")
            .RequireTenantMember()
            .DisallowImpersonation();

        current.MapPost("/export", async (ISender sender, CancellationToken ct) =>
            {
                var export = await sender.Send(new ExportTenantDataCommand(), ct);
                return Results.File(export.Content, "application/zip", export.FileName);
            })
            .WithName("ExportTenantData")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .WithSummary("Export tenant data")
            .WithDescription("Owner only. Zip: control-plane data as JSON + every tenant table as CSV. Audited.")
            .RequireTenantMember()
            .DisallowImpersonation()
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        current.MapPost("/deletion", async (TenantReasonRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RequestTenantDeletionCommand(request.Reason), ct))))
            .WithName("RequestTenantDeletion").Produces<ApiResponse<RequestTenantDeletionResult>>()
            .WithSummary("Delete tenant")
            .WithDescription("Owner only. The tenant becomes unavailable now and is purged after the grace period.")
            .RequireTenantMember()
            .DisallowImpersonation();

        // ----- platform -----
        var host = app.MapGroup(root + "/host/tenants").WithTags(HostTag).WithApiVersionSet(versionSet)
            .HasApiVersion(1.0).HostOnly();

        host.MapGet("/", async ([AsParameters] GetHostTenantsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetHostTenants").Produces<ApiResponse<PaginatedResult<HostTenantDto>>>()
            .WithSummary("List tenants (platform)")
            .RequirePermission(Permissions.TenantsRead);

        host.MapPost("/{id:guid}/suspend", async (Guid id, TenantReasonRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new SuspendTenantCommand(id, request.Reason ?? string.Empty), ct))))
            .WithName("SuspendTenant").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Suspend tenant (platform)")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/reactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new ReactivateTenantCommand(id), ct))))
            .WithName("ReactivateTenant").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Reactivate tenant (platform)")
            .RequirePermission(Permissions.TenantsManage);

        host.MapDelete("/{id:guid}", async (Guid id, [FromQuery] string? reason, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteTenantCommand(id, reason), ct))))
            .WithName("DeleteTenant").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Delete tenant (platform)").WithDescription("Starts the deletion grace period.")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/cancel-deletion", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new CancelTenantDeletionCommand(id), ct))))
            .WithName("CancelTenantDeletion").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Cancel tenant deletion (platform)").WithDescription("The tenant stays suspended until reactivated.")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPut("/{id:guid}/isolation", async (Guid id, ChangeIsolationRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new ChangeTenantIsolationCommand(id, request.Isolation, request.ConnectionSecretRef), ct))))
            .WithName("ChangeTenantIsolation").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Change isolation (platform)")
            .WithDescription("Metadata only (data copied by hand, see the runbook); the tenant must be suspended. " +
                             "Prefer POST /{id}/move, which copies the data automatically.")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/move", async (Guid id, ChangeIsolationRequest request, ISender sender, CancellationToken ct) =>
                Results.Accepted((string?)null, ApiResponseExtensions.Success(await sender.Send(
                    new MoveTenantCommand(id, request.Isolation, request.ConnectionSecretRef), ct))))
            .WithName("MoveTenant").Produces<ApiResponse<TenantStatusResult>>(StatusCodes.Status202Accepted)
            .WithSummary("Move tenant data (platform)")
            .WithDescription("Shared ⇄ dedicated (or another dedicated server), automated: the tenant is read-only " +
                             "(Migrating) while its rows are copied and verified in the background; then it is switched " +
                             "and the source is cleaned. Watch the tenant audit trail (tenant.move-*).")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/move/retry", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Accepted((string?)null, ApiResponseExtensions.Success(await sender.Send(new RetryTenantMoveCommand(id), ct))))
            .WithName("RetryTenantMove").Produces<ApiResponse<TenantStatusResult>>(StatusCodes.Status202Accepted)
            .WithSummary("Retry a stuck move (platform)")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/move/abort", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new AbortTenantMoveCommand(id), ct))))
            .WithName("AbortTenantMove").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Abort a stuck move (platform)")
            .WithDescription("The tenant stays where it was; a copy still running fails safely and is cleaned up.")
            .RequirePermission(Permissions.TenantsManage);

        host.MapPost("/{id:guid}/provision", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RetryTenantProvisioningCommand(id), ct))))
            .WithName("RetryTenantProvisioning").Produces<ApiResponse<TenantStatusResult>>()
            .WithSummary("Re-run provisioning (platform)")
            .RequirePermission(Permissions.TenantsManage);
    }
}
