using Carter;
using Catalog.Contracts.Products.Queries;
using UserDirectory.Contracts.Queries;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Core.Authorization;
using Shared.Core.Response;
using Shared.Core.Web;
using Subscriptions.Contracts.Ledger.DTOs;
using Subscriptions.Contracts.Ledger.Queries;

namespace Api.Endpoints;

public class DashboardEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/dashboard/stats",
                async (ISender sender, IHttpContextService httpContextService, CancellationToken cancellationToken) =>
                {
                    var userStats = await sender.Send(new GetUserStatsQuery(), cancellationToken);

                    // Cross-module contract query: Catalog exposes stats via Catalog.Contracts only.
                    var productStats = await sender.Send(new GetProductStatsQuery(), cancellationToken);

                    // Billing KPIs only for callers allowed to read billing (cached 5 min by the Subscriptions module;
                    // full figures: GET admin/billing/dashboard).
                    BillingDashboardDto? billing = null;
                    if (httpContextService.HasClaim(Permissions.ClaimType, Permissions.BillingRead))
                    {
                        billing = await sender.Send(new GetBillingDashboardQuery(), cancellationToken);
                    }

                    var result = new DashboardStatsDto
                    {
                        TotalUsers = userStats.TotalUsers,
                        TotalProducts = productStats.TotalProducts,
                        PendingReviewProducts = productStats.PendingReviewProducts,
                        PublishedProducts = productStats.PublishedProducts,
                        FeaturedProducts = productStats.FeaturedProducts,
                        RecurringRevenue = billing?.RecurringRevenue,
                        PendingPaymentProofs = billing?.PendingProofs,
                        PastDueSubscriptions = billing?.PastDueCount
                    };

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetDashboardStats")
            .WithApiVersionSet(app.NewApiVersionSet("Dashboard").Build())
            .WithTags("Dashboard")
            .Produces<ApiResponse<DashboardStatsDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Dashboard Stats")
            .WithDescription("Aggregate admin dashboard metrics. Billing figures are included only with the billing.read permission. Extend DashboardStatsDto with stats from other modules.")
            .HasApiVersion(1.0);
    }
}

public class DashboardStatsDto
{
    public int TotalUsers { get; init; }
    public int TotalProducts { get; init; }
    public int PendingReviewProducts { get; init; }
    public int PublishedProducts { get; init; }
    public int FeaturedProducts { get; init; }

    /// <summary>MRR/ARR per currency (null without billing.read).</summary>
    public IReadOnlyList<RecurringRevenueDto>? RecurringRevenue { get; init; }

    /// <summary>Submitted payment proofs awaiting review (null without billing.read).</summary>
    public int? PendingPaymentProofs { get; init; }

    /// <summary>Past-due subscriptions (null without billing.read).</summary>
    public int? PastDueSubscriptions { get; init; }
}
