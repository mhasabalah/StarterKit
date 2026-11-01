using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;
using Shared.Contracts.Pagination;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class GetAdminInvoicesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/invoices",
                async ([AsParameters] GetAdminInvoicesQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetAdminInvoices")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<AdminInvoiceDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Admin Invoices")
            .WithDescription("Retrieve paginated invoices, optionally filtered by subscriber, status, or type. Admin only.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
