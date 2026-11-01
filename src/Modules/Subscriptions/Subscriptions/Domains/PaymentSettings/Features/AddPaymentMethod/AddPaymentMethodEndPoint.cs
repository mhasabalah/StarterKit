using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.AddPaymentMethod;

public class AddPaymentMethodEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/payment-methods",
                async (AddPaymentMethodCommand command, ISender sender, CancellationToken ct) =>
                {
                    var id = await sender.Send(command, ct);
                    return Results.Created(
                        $"{EndpointConfig.BaseApiPath}/admin/payment-methods/{id}",
                        ApiResponseExtensions.Success(new { id }));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("AddPaymentMethod")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces(StatusCodes.Status201Created)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
