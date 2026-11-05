namespace Webhooks.Domains.Deliveries.Features;

/// <summary>Delivery log of the current tenant's webhook endpoints (permission <c>webhooks.manage</c>).</summary>
public class WebhookDeliveriesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/webhooks/deliveries")
            .WithTags("Webhooks")
            .WithApiVersionSet(app.NewApiVersionSet("Webhooks").Build())
            .HasApiVersion(1.0);

        group.MapGet("/", async ([AsParameters] GetWebhookDeliveriesQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetWebhookDeliveries").Produces<ApiResponse<PaginatedResult<WebhookDeliveryDto>>>()
            .WithSummary("List webhook deliveries")
            .WithDescription("Newest first. Filters: endpointId, status, eventType.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetWebhookDeliveryByIdQuery(id), ct))))
            .WithName("GetWebhookDeliveryById").Produces<ApiResponse<WebhookDeliveryDetailDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Webhook delivery")
            .WithDescription("Includes the exact payload and the truncated last response.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);

        group.MapPost("/{id:guid}/redeliver", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RedeliverWebhookCommand(id), ct))))
            .WithName("RedeliverWebhook").Produces<ApiResponse<RedeliverWebhookResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Redeliver")
            .WithDescription("Queues a new delivery of the same event (same `webhook-id`) to the same endpoint.")
            .RequireTenantPermission(TenantPermissions.WebhooksManage);
    }
}
