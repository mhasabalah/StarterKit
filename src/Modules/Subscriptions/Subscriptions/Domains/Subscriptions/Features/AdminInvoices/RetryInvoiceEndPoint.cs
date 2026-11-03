using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class RetryInvoiceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/invoices/{id:guid}/retry",
                async (Guid id,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new RetryInvoiceCommand(id, adminId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Retried" }));
                })
            .RequirePermission(Permissions.BillingApprove)
            .WithName("RetryInvoice")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
