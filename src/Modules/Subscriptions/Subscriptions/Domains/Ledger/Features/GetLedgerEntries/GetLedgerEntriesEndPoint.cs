namespace Subscriptions.Domains.Ledger.Features.GetLedgerEntries;

public class GetLedgerEntriesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/billing/ledger",
                async ([AsParameters] GetLedgerEntriesQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .RequireRateLimiting("search")
            .WithName("GetLedgerEntries")
            .WithApiVersionSet(app.NewApiVersionSet("Billing").Build())
            .WithTags("Billing")
            .Produces<ApiResponse<PaginatedResult<LedgerEntryDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Billing ledger")
            .WithDescription("Immutable money ledger (payments, refunds, credit notes, adjustments). Filters: date range [from, to), type, subscriber, subscription, currency.")
            .HasApiVersion(1.0);
    }
}
