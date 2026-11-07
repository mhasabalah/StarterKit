using Shared.Core.MultiTenancy.Web;

namespace Subscriptions.Domains.OnlinePayments.Features.RemoveSavedPaymentMethod;

public class RemoveSavedPaymentMethodEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/subscriptions/saved-payment-methods/{methodId:guid}",
                async (Guid methodId, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new RemoveSavedPaymentMethodCommand(subscriberId, methodId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { methodId }));
                })
            .RequireAuthorization()
            .DisallowImpersonation()
            .DisallowApiKey()
            .RequireRateLimiting("create")
            .WithName("RemoveSavedPaymentMethod")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
