namespace Subscriptions.Domains.OnlinePayments.Features.GetSavedPaymentMethods;

public class GetSavedPaymentMethodsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // The subscriber provider requires the tenant billing permission in multi-tenant mode.
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/saved-payment-methods",
                async (ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetSavedPaymentMethodsQuery(subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .DisallowApiKey()
            .WithName("GetSavedPaymentMethods")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<List<SavedPaymentMethodDto>>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
