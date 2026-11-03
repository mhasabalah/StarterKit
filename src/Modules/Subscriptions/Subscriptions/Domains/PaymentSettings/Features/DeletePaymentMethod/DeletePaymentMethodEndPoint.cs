using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.DeletePaymentMethod;

public class DeletePaymentMethodEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/admin/payment-methods/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new DeletePaymentMethodCommand(id), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Payment method deleted successfully"));
                })
            .RequirePermission(Permissions.PaymentSettingsManage)
            .WithName("DeletePaymentMethod")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
