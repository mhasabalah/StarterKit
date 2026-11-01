using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.UpsertPaymentInstructions;

public class UpsertPaymentInstructionsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/admin/payment-instructions",
                async (UpsertPaymentInstructionsCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.Ok(ApiResponseExtensions.Success("Payment instructions updated successfully"));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpsertPaymentInstructions")
            .WithApiVersionSet(app.NewApiVersionSet("Payment Methods").Build())
            .WithTags("Payment Methods")
            .Produces(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
