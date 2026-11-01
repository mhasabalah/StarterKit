using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentById;

public class GetPaymentByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/payments/{paymentId:guid}",
                async (Guid paymentId, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetPaymentByIdQuery(paymentId), ct);
                    return result is null
                        ? ApiResponseExtensions.NotFound("Payment not found")
                        : Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetPaymentById")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<AdminPaymentDto>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
