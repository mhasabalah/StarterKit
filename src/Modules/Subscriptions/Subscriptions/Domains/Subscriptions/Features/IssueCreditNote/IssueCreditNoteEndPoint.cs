using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.IssueCreditNote;

/// <param name="Amount">Gross amount to credit; omit to credit everything still creditable.</param>
/// <param name="Version">The original invoice version (AdminInvoiceDto.Version).</param>
public record IssueCreditNoteRequest(string Reason, decimal? Amount = null, long? Version = null);

public class IssueCreditNoteEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/invoices/{id:guid}/credit-note",
                async (Guid id, IssueCreditNoteRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new IssueCreditNoteCommand(id, adminId, request.Reason, request.Amount, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRefund)
            .RequireRateLimiting("create")
            .WithIdempotency()
            .WithName("IssueCreditNote")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<IssueCreditNoteResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
