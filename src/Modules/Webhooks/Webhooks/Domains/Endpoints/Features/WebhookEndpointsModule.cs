namespace Webhooks.Domains.Endpoints.Features;

public record UpdateWebhookEndpointRequest(
    string Url, string? Description, IReadOnlyList<string> EventTypes, bool Enabled = true, long? Version = null);

/// <summary>
/// Webhook endpoint management for the current tenant. Every route needs the tenant permission
/// <c>webhooks.manage</c> (membership based, not JWT). Routes that reveal or replace the signing secret, and deletes, are
/// not available while a platform operator impersonates a user.
/// </summary>
public class WebhookEndpointsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/webhooks")
            .WithTags("Webhooks")
            .WithApiVersionSet(app.NewApiVersionSet("Webhooks").Build())
            .HasApiVersion(1.0);

        group.MapGet("/event-types", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetWebhookEventTypesQuery(), ct))))
            .WithName("GetWebhookEventTypes").Produces<ApiResponse<IReadOnlyList<WebhookEventTypeDto>>>()
            .WithSummary("Webhook event types")
            .WithDescription("The public events an endpoint can subscribe to (use `*` for all).")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);

        group.MapGet("/endpoints", async ([AsParameters] GetWebhookEndpointsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetWebhookEndpoints").Produces<ApiResponse<PaginatedResult<WebhookEndpointDto>>>()
            .WithSummary("List webhook endpoints")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);

        group.MapGet("/endpoints/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetWebhookEndpointByIdQuery(id), ct))))
            .WithName("GetWebhookEndpointById").Produces<ApiResponse<WebhookEndpointDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Webhook endpoint")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);

        group.MapPost("/endpoints", async (CreateWebhookEndpointCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"api/v1/webhooks/endpoints/{result.Id}", ApiResponseExtensions.Success(result));
            })
            .WithName("CreateWebhookEndpoint").Produces<ApiResponse<CreateWebhookEndpointResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create webhook endpoint")
            .WithDescription("Returns the signing secret ONCE (`whsec_...`). Deliveries follow the Standard Webhooks signature scheme.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage)
            .DisallowImpersonation();

        group.MapPut("/endpoints/{id:guid}", async (Guid id, UpdateWebhookEndpointRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new UpdateWebhookEndpointCommand(id, request.Url, request.Description, request.EventTypes, request.Enabled,
                        request.Version), ct))))
            .WithName("UpdateWebhookEndpoint").Produces<ApiResponse<UpdateWebhookEndpointResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update webhook endpoint")
            .WithDescription("`enabled: true` re-enables an auto-disabled endpoint and resets its failure counter.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage)
            .DisallowImpersonation(); // a URL change redirects the signed event stream

        group.MapDelete("/endpoints/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteWebhookEndpointCommand(id), ct))))
            .WithName("DeleteWebhookEndpoint").Produces<ApiResponse<DeleteWebhookEndpointResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete webhook endpoint")
            .WithDescription("Deletes the endpoint and its delivery history.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage)
            .DisallowImpersonation();

        group.MapPost("/endpoints/{id:guid}/rotate-secret", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RotateWebhookEndpointSecretCommand(id), ct))))
            .WithName("RotateWebhookEndpointSecret").Produces<ApiResponse<RotateWebhookEndpointSecretResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Rotate signing secret")
            .WithDescription("Issues a new secret (returned ONCE); the old one stops working immediately.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage)
            .DisallowImpersonation();

        group.MapPost("/endpoints/{id:guid}/ping", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new SendWebhookTestPingCommand(id), ct))))
            .WithName("SendWebhookTestPing").Produces<ApiResponse<SendWebhookTestPingResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Send a test ping")
            .WithDescription("Queues a signed `webhook.ping` delivery; follow it in the delivery log.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);
    }
}
