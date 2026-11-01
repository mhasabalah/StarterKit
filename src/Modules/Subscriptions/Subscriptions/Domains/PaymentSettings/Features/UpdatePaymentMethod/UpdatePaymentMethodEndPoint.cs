using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.UpdatePaymentMethod;

public class UpdatePaymentMethodEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/admin/payment-methods/{id:guid}",
                async (Guid id, UpdatePaymentMethodCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command with { Id = id }, ct);
                    return Results.Ok(ApiResponseExtensions.Success("Payment method updated successfully"));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpdatePaymentMethod")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
