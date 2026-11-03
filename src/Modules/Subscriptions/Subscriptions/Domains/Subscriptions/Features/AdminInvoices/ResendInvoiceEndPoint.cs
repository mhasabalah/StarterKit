using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class ResendInvoiceEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/invoices/{id:guid}/resend",
                async (Guid id,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new ResendInvoiceCommand(id, adminId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Resent" }));
                })
            .RequirePermission(Permissions.BillingApprove)
            .WithName("ResendInvoice")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
