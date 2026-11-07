using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.OnlinePayments.Features.CreateOnlineCheckout;

public record CreateOnlineCheckoutRequest(string Provider, string SuccessUrl, string CancelUrl, Guid? PaymentId = null, bool SaveCard = false);

public class CreateOnlineCheckoutEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{subscriptionId:guid}/online-payment",
                async (Guid subscriptionId, CreateOnlineCheckoutRequest request, ISubscriberIdProvider subscribers,
                    ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new CreateOnlineCheckoutCommand(
                        subscriptionId, subscriberId, request.Provider, request.SuccessUrl, request.CancelUrl,
                        request.PaymentId, request.SaveCard), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithName("CreateOnlineCheckout")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<CreateOnlineCheckoutResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
