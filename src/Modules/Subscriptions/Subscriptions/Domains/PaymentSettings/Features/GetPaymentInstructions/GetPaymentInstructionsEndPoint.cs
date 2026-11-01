using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.PaymentSettings.Features.GetPaymentInstructions;

public class GetPaymentInstructionsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/payment-instructions",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetPaymentInstructionsQuery(), ct);
                    return result is null
                        ? ApiResponseExtensions.NotFound("Payment instructions not configured")
                        : Results.Ok(ApiResponseExtensions.Success(result));
                })
            .AllowAnonymous()
            .WithName("GetPaymentInstructions")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces<ApiResponse<PaymentInstructionsDto>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
