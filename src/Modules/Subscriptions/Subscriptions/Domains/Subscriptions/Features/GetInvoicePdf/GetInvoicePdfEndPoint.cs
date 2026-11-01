using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetInvoicePdf;

public class GetInvoicePdfEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/invoices/{id:guid}/pdf",
                async (Guid id,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var actorId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");

                    var isAdmin = httpContextService.HasAnyRole(RoleConstants.Admin, RoleConstants.SuperAdmin);

                    var result = await sender.Send(new GetInvoicePdfQuery(id, actorId, isAdmin), ct);
                    return Results.Redirect(result.FileUrl);
                })
            .RequireAuthorization()
            .WithName("GetInvoicePdf")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Invoices")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
