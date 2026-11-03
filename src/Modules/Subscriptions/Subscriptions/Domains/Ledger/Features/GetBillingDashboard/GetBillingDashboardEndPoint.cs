namespace Subscriptions.Domains.Ledger.Features.GetBillingDashboard;

public class GetBillingDashboardEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/billing/dashboard",
                async ([AsParameters] GetBillingDashboardQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .RequireRateLimiting("search")
            .WithName("GetBillingDashboard")
            .WithApiVersionSet(app.NewApiVersionSet("Billing").Build())
            .WithTags("Billing")
            .Produces<ApiResponse<BillingDashboardDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Billing dashboard")
            .WithDescription("MRR/ARR per currency, status counts, revenue (gross/refunds/net from the ledger) by plan and payment method for [from, to) (default: current UTC month), proof queue size and oldest age, past-due, churn, new subscriptions. Cached 5 minutes per range.")
            .HasApiVersion(1.0);
    }
}
