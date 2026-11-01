using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.PaymentSettings.Features.GetPaymentMethods;

public class GetPaymentMethodsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/payment-methods",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetPaymentMethodsQuery(), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .AllowAnonymous()
            .WithName("GetPaymentMethods")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces<ApiResponse<IReadOnlyList<PaymentMethodDto>>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);

        app.MapGet(EndpointConfig.BaseApiPath + "/admin/payment-methods",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetAdminPaymentMethodsQuery(), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetAdminPaymentMethods")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces<ApiResponse<IReadOnlyList<PaymentMethodDto>>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
