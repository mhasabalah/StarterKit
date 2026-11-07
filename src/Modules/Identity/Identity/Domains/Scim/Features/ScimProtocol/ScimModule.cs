using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Identity.Contracts.Scim;
using Identity.Services.Scim;
using Identity.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy;

namespace Identity.Domains.Scim.Features.ScimProtocol;

/// <summary>
/// SCIM 2.0 service provider at <c>/scim/v2</c> (Users, Groups, discovery). Authenticated ONLY by a directory
/// bearer token (policy <c>scim</c>); every request runs in that directory's tenant. Bodies are
/// <c>application/scim+json</c> (or plain JSON), errors use SCIM's own error schema.
/// </summary>
public class ScimModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ScimAuthentication.RoutePrefix)
            .WithTags("SCIM 2.0")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .HasApiVersion(1.0)
            .RequireAuthorization(ScimAuthentication.PolicyName)
            .DisableAntiforgery();

        group.MapGet("ServiceProviderConfig", () => Scim(ScimDiscovery.ServiceProviderConfig()))
            .WithName("ScimServiceProviderConfig").WithSummary("SCIM capabilities");
        group.MapGet("ResourceTypes", () => Scim(ScimDiscovery.ResourceTypes()))
            .WithName("ScimResourceTypes").WithSummary("SCIM resource types");
        group.MapGet("Schemas", () => Scim(ScimDiscovery.Schemas()))
            .WithName("ScimSchemas").WithSummary("SCIM schemas");

        group.MapGet("Users", (HttpContext http, ISender sender, [FromQuery] string? filter, [FromQuery] int? startIndex,
                [FromQuery] int? count, CancellationToken ct) =>
                Run(http, sender, d => new GetScimUsersQuery(d, filter, startIndex, count), ct))
            .WithName("ScimListUsers").WithSummary("List / filter users");
        group.MapGet("Users/{id:guid}", (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
                Run(http, sender, d => new GetScimUserQuery(d, id), ct))
            .WithName("ScimGetUser").WithSummary("Get a user");
        group.MapPost("Users", async (HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new CreateScimUserCommand(d, body), ct);
            })
            .WithName("ScimCreateUser").WithSummary("Provision a user");
        group.MapPut("Users/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new ReplaceScimUserCommand(d, id, body), ct);
            })
            .WithName("ScimReplaceUser").WithSummary("Replace a user");
        group.MapPatch("Users/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new PatchScimUserCommand(d, id, body), ct);
            })
            .WithName("ScimPatchUser").WithSummary("Patch a user (active=false deprovisions)");
        group.MapDelete("Users/{id:guid}", (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
                Run(http, sender, d => new DeleteScimUserCommand(d, id), ct))
            .WithName("ScimDeleteUser").WithSummary("Deprovision a user");

        group.MapGet("Groups", (HttpContext http, ISender sender, [FromQuery] string? filter, [FromQuery] int? startIndex,
                [FromQuery] int? count, [FromQuery] string? excludedAttributes, CancellationToken ct) =>
                Run(http, sender, d => new GetScimGroupsResourceQuery(d, filter, startIndex, count, ExcludesMembers(excludedAttributes)), ct))
            .WithName("ScimListGroups").WithSummary("List / filter groups");
        group.MapGet("Groups/{id:guid}", (Guid id, HttpContext http, ISender sender, [FromQuery] string? excludedAttributes,
                CancellationToken ct) =>
                Run(http, sender, d => new GetScimGroupQuery(d, id, ExcludesMembers(excludedAttributes)), ct))
            .WithName("ScimGetGroup").WithSummary("Get a group");
        group.MapPost("Groups", async (HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new CreateScimGroupCommand(d, body), ct);
            })
            .WithName("ScimCreateGroup").WithSummary("Create a group");
        group.MapPut("Groups/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new ReplaceScimGroupCommand(d, id, body), ct);
            })
            .WithName("ScimReplaceGroup").WithSummary("Replace a group");
        group.MapPatch("Groups/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return await Run(http, sender, d => new PatchScimGroupCommand(d, id, body), ct);
            })
            .WithName("ScimPatchGroup").WithSummary("Patch a group (members add/remove)");
        group.MapDelete("Groups/{id:guid}", (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
                Run(http, sender, d => new DeleteScimGroupCommand(d, id), ct))
            .WithName("ScimDeleteGroup").WithSummary("Delete a group");
    }

    private static bool ExcludesMembers(string? excludedAttributes) =>
        excludedAttributes?.Split(',').Any(a => a.Trim().Equals("members", StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task<JsonObject> ReadAsync(HttpContext http, CancellationToken ct)
    {
        try
        {
            return await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct) as JsonObject
                   ?? throw ScimException.InvalidSyntax("The body must be a JSON object.");
        }
        catch (JsonException)
        {
            throw ScimException.InvalidSyntax("The body is not valid JSON.");
        }
    }

    /// <summary>Directory + tenant from the token, then the operation; every failure becomes a SCIM error.</summary>
    private static async Task<IResult> Run(
        HttpContext http, ISender sender, Func<Guid, IRequest<ScimResult>> build, CancellationToken ct)
    {
        try
        {
            var directoryId = ScimAuthentication.DirectoryId(http.User);
            var tenantStore = http.RequestServices.GetRequiredService<ITenantStore>();
            var tenant = await tenantStore.FindByIdAsync(ScimAuthentication.TenantId(http.User), ct)
                         ?? throw new ScimException(403, "The organization of this directory is not available.");

            if (!http.RequestServices.GetRequiredService<IOptions<SsoOptions>>().Value.Enabled)
                throw new ScimException(403, "SCIM provisioning is disabled on this platform.");

            ScimResult result;
            using (TenantScope.Begin(tenant))
            {
                // A tenant whose plan lost the sso feature stops provisioning immediately.
                var entitlements = http.RequestServices.GetRequiredService<IEntitlementService>();
                if (!await entitlements.HasFeatureAsync(EntitlementKeys.Sso, ct))
                    throw new ScimException(403, "The organization plan does not include directory provisioning.");

                result = await sender.Send(build(directoryId), ct);
            }

            if (result.Location is not null && result.Status == StatusCodes.Status201Created)
                http.Response.Headers.Location = result.Location;
            if (result.ETag is not null)
                http.Response.Headers.ETag = result.ETag;

            return result.Body is null ? Results.StatusCode(result.Status) : Scim(result.Body, result.Status);
        }
        catch (ScimException ex)
        {
            return Scim(ScimJson.Error(ex.Status, ex.Message, ex.ScimType), ex.Status);
        }
        catch (FluentValidation.ValidationException ex)
        {
            return Scim(ScimJson.Error(400, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)), "invalidValue"), 400);
        }
        catch (CustomException ex)
        {
            // e.g. the plan's MaxUsers (403) from Tenancy: the IdP shows this text to the customer's admin.
            var status = (int)ex.StatusCode is >= 400 and < 600 ? (int)ex.StatusCode : (int)HttpStatusCode.InternalServerError;
            return Scim(ScimJson.Error(status, ex.Message, null), status);
        }
    }

    private static IResult Scim(JsonNode body, int status = StatusCodes.Status200OK) =>
        Results.Text(body.ToJsonString(), ScimSchemas.ContentType, Encoding.UTF8, status);
}

/// <summary>Static discovery documents (RFC 7644 §4). Advertises exactly what is implemented.</summary>
public static class ScimDiscovery
{
    public static JsonObject ServiceProviderConfig() => new()
    {
        ["schemas"] = new JsonArray(ScimSchemas.ServiceProviderConfig),
        ["patch"] = new JsonObject { ["supported"] = true },
        ["bulk"] = new JsonObject { ["supported"] = false, ["maxOperations"] = 0, ["maxPayloadSize"] = 0 },
        ["filter"] = new JsonObject { ["supported"] = true, ["maxResults"] = ScimProvisioningService.MaxPageSize },
        ["changePassword"] = new JsonObject { ["supported"] = false },
        ["sort"] = new JsonObject { ["supported"] = false },
        ["etag"] = new JsonObject { ["supported"] = false },
        ["authenticationSchemes"] = new JsonArray(new JsonObject
        {
            ["type"] = "oauthbearertoken",
            ["name"] = "OAuth Bearer Token",
            ["description"] = "Directory token created under the organization's SCIM settings.",
            ["primary"] = true
        })
    };

    public static JsonObject ResourceTypes() => ScimJson.List(
    [
        ResourceType("User", "/Users", ScimSchemas.User, ScimSchemas.EnterpriseUser),
        ResourceType("Group", "/Groups", ScimSchemas.Group)
    ], 2, 1);

    public static JsonObject Schemas() => ScimJson.List(
    [
        new JsonObject { ["id"] = ScimSchemas.User, ["name"] = "User" },
        new JsonObject { ["id"] = ScimSchemas.EnterpriseUser, ["name"] = "EnterpriseUser" },
        new JsonObject { ["id"] = ScimSchemas.Group, ["name"] = "Group" }
    ], 3, 1);

    private static JsonObject ResourceType(string name, string endpoint, string schema, string? extension = null)
    {
        var resourceType = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimSchemas.ResourceType),
            ["id"] = name,
            ["name"] = name,
            ["endpoint"] = endpoint,
            ["schema"] = schema
        };
        if (extension is not null)
        {
            resourceType["schemaExtensions"] = new JsonArray(new JsonObject
            {
                ["schema"] = extension,
                ["required"] = false
            });
        }

        return resourceType;
    }
}
