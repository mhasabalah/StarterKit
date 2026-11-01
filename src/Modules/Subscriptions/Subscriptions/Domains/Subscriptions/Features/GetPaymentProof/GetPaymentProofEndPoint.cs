namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentProof;

public class GetPaymentProofEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/payments/{paymentId:guid}/proof",
                async (Guid paymentId, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var actorId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetPaymentProofQuery(paymentId, actorId, false), ct);
                    return Results.Redirect(result.FileUrl);
                })
            .RequireAuthorization()
            .WithName("GetPaymentProofForSubscriber")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .WithOpenApi()
            .HasApiVersion(1.0);

        app.MapGet(EndpointConfig.BaseApiPath + "/admin/payments/{paymentId:guid}/proof",
                async (Guid paymentId, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var actorId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetPaymentProofQuery(paymentId, actorId, true), ct);
                    return Results.Redirect(result.FileUrl);
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("GetPaymentProofForAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
