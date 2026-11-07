using Shared.Core.MultiTenancy.Web;

namespace Subscriptions.Domains.OnlinePayments.Features.SetDefaultSavedPaymentMethod;

public class SetDefaultSavedPaymentMethodEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/saved-payment-methods/{methodId:guid}/default",
                async (Guid methodId, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new SetDefaultSavedPaymentMethodCommand(subscriberId, methodId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { methodId }));
                })
            .RequireAuthorization()
            .DisallowImpersonation()
            .DisallowApiKey()
            .RequireRateLimiting("create")
            .WithName("SetDefaultSavedPaymentMethod")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
