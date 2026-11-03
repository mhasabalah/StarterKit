using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RecordManualPayment;

public record RecordManualPaymentRequest(
    decimal Amount,
    string Method,
    string? Reference = null,
    DateTimeOffset? ReceivedAt = null,
    string? Note = null,
    Guid? PaymentId = null,
    long? Version = null);

public class RecordManualPaymentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/payments/manual",
                async (Guid id, RecordManualPaymentRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new RecordManualPaymentCommand(
                        id,
                        adminId,
                        request.Amount,
                        request.Method,
                        request.Reference,
                        request.ReceivedAt,
                        request.Note,
                        request.PaymentId,
                        request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("RecordManualPayment")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<RecordManualPaymentResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Record a manual / partial payment")
            .WithDescription("Records money received offline. Partial amounts accumulate; the receipt that completes the amount due activates/renews the subscription and issues the invoice. Overpayment is rejected unless configured.")
            .HasApiVersion(1.0);
    }
}
