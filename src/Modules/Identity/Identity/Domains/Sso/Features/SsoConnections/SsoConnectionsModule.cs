using Identity.Contracts.Sso;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.Sso.Features.SsoConnections;

public record UpdateSsoConnectionRequest(SsoConnectionInput Input, long? Version = null);

public record SsoVersionRequest(long? Version = null);

public record AddSsoDomainRequest(string Domain);

public record StartSsoTestRequest(string ReturnUrl);

/// <summary>
/// Enterprise SSO administration for the CURRENT tenant. Needs the tenant permission <c>tenant.sso.manage</c> and
/// the plan feature <c>sso</c>. Changes are refused while impersonating (they decide how a whole company signs in)
/// and for API keys (identity configuration is a human, interactive task).
/// </summary>
public class SsoConnectionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/tenant/sso")
            .WithTags("Enterprise SSO")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .HasApiVersion(1.0);

        group.MapGet("/connections", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetSsoConnectionsQuery(), ct))))
            .WithName("GetSsoConnections").WithSummary("List SSO connections")
            .Produces<ApiResponse<List<SsoConnectionDto>>>()
            .Read();

        group.MapGet("/connections/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetSsoConnectionByIdQuery(id), ct))))
            .WithName("GetSsoConnectionById").WithSummary("SSO connection")
            .WithDescription("Includes the service-provider values to paste into the IdP (redirect URI / ACS URL / entity id).")
            .Produces<ApiResponse<SsoConnectionDto>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Read();

        group.MapPost("/connections", async (SsoConnectionInput input, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateSsoConnectionCommand(input), ct);
                return Results.Created($"api/v1/tenant/sso/connections/{created.Id}", ApiResponse.Success(created));
            })
            .WithName("CreateSsoConnection").WithSummary("Create an SSO connection (draft)")
            .WithDescription("OIDC: authority + client id + client secret (write-only). SAML: IdP entity id, SSO URL and " +
                             "signing certificate(s). Add and verify an email domain, test, then activate.")
            .Produces<ApiResponse<SsoConnectionDto>>(StatusCodes.Status201Created)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Write();

        group.MapPut("/connections/{id:guid}", async (Guid id, UpdateSsoConnectionRequest request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(
                    await sender.Send(new UpdateSsoConnectionCommand(id, request.Input, request.Version), ct))))
            .WithName("UpdateSsoConnection").WithSummary("Update an SSO connection")
            .WithDescription("A null clientSecret keeps the stored one. Send version to avoid lost updates (409).")
            .Produces<ApiResponse<SsoConnectionDto>>()
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .Write();

        group.MapPost("/connections/{id:guid}/activate", async (Guid id, SsoVersionRequest? request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new ActivateSsoConnectionCommand(id, request?.Version), ct))))
            .WithName("ActivateSsoConnection").WithSummary("Activate an SSO connection")
            .WithDescription("Needs a complete configuration and at least one verified domain.")
            .Produces<ApiResponse<SsoConnectionDto>>()
            .Write();

        group.MapPost("/connections/{id:guid}/disable", async (Guid id, SsoVersionRequest? request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new DisableSsoConnectionCommand(id, request?.Version), ct))))
            .WithName("DisableSsoConnection").WithSummary("Disable an SSO connection")
            .WithDescription("Sign-in through it stops immediately; enforcement is lifted (users fall back to other methods).")
            .Produces<ApiResponse<SsoConnectionDto>>()
            .Write();

        group.MapDelete("/connections/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new DeleteSsoConnectionCommand(id), ct))))
            .WithName("DeleteSsoConnection").WithSummary("Delete an SSO connection")
            .WithDescription("Removes the connection and its identity links; accounts and memberships stay.")
            .Produces<ApiResponse<DeleteSsoConnectionResult>>()
            .Write();

        group.MapPost("/connections/{id:guid}/refresh-metadata", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RefreshSamlMetadataCommand(id), ct))))
            .WithName("RefreshSamlMetadata").WithSummary("Refresh the SAML metadata now")
            .WithDescription("Re-reads the connection's IdP metadata URL (certificates and SSO URL). The metadata's entity " +
                             "id must equal the configured one. The outcome (Updated / Unchanged / Failed + error code) is " +
                             "in the result; the same refresh also runs on a schedule.")
            .Produces<ApiResponse<RefreshSamlMetadataResult>>()
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Write()
            .RequireRateLimiting("create");

        group.MapPost("/connections/{id:guid}/domains", async (Guid id, AddSsoDomainRequest request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new AddSsoDomainCommand(id, request.Domain), ct))))
            .WithName("AddSsoDomain").WithSummary("Add an email domain")
            .WithDescription("Returns the DNS TXT record to publish (recordName / recordValue).")
            .Produces<ApiResponse<SsoDomainDto>>()
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .Write();

        group.MapPost("/connections/{id:guid}/domains/{domain}/verify", async (Guid id, string domain, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new VerifySsoDomainCommand(id, domain), ct))))
            .WithName("VerifySsoDomain").WithSummary("Verify an email domain (DNS TXT)")
            .WithDescription("409 sso_domain_unverified while the record is missing; 409 sso_domain_taken when another " +
                             "connection already verified it.")
            .Produces<ApiResponse<SsoDomainDto>>()
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .Write()
            .RequireRateLimiting("create");

        group.MapDelete("/connections/{id:guid}/domains/{domain}", async (Guid id, string domain, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RemoveSsoDomainCommand(id, domain), ct))))
            .WithName("RemoveSsoDomain").WithSummary("Remove an email domain")
            .Produces<ApiResponse<RemoveSsoDomainResult>>()
            .Write();

        group.MapPost("/connections/{id:guid}/test", async (Guid id, StartSsoTestRequest request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new StartSsoTestCommand(id, request.ReturnUrl), ct))))
            .WithName("StartSsoTest").WithSummary("Test an SSO connection")
            .WithDescription("Returns the IdP URL to open. The IdP round trip signs nobody in; it returns to returnUrl " +
                             "with sso_test={code}. Works for draft connections.")
            .Produces<ApiResponse<SsoRedirect>>()
            .Write();

        group.MapGet("/test-results/{code}", async (string code, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetSsoTestReportQuery(code), ct))))
            .WithName("GetSsoTestReport").WithSummary("Result of an SSO connection test")
            .WithDescription("The subject, email and every claim/attribute the IdP sent. Readable once.")
            .Produces<ApiResponse<SsoTestReportDto>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Read();
    }
}

/// <summary>Platform staff overview of every tenant's connections (read only).</summary>
public class SsoAdminModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/sso/connections",
                async ([AsParameters] GetAllSsoConnectionsQuery query, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponse.Success(await sender.Send(query, ct))))
            .WithName("GetAllSsoConnections")
            .WithTags("Enterprise SSO")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .HasApiVersion(1.0)
            .WithSummary("All SSO connections (platform)")
            .Produces<ApiResponse<PaginatedResult<SsoConnectionSummaryDto>>>()
            .RequirePermission(Permissions.TenantsRead);
    }
}

internal static class SsoEndpointConventions
{
    public static RouteHandlerBuilder Read(this RouteHandlerBuilder builder) => builder
        .RequireTenantPermission(TenantPermissions.SsoManage)
        .RequireFeature(EntitlementKeys.Sso)
        .DisallowApiKey();

    public static RouteHandlerBuilder Write(this RouteHandlerBuilder builder) => builder
        .Read()
        .DisallowImpersonation();
}
