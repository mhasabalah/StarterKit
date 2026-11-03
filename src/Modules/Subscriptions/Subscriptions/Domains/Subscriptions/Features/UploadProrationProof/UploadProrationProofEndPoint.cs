using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.UploadProrationProof;

public class UploadProrationProofEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{subscriptionId:guid}/proration/proof",
                async (Guid subscriptionId,
                    [FromForm] IFormFile file,
                    [FromForm] Guid paymentId,
                    [FromForm] Guid paymentMethodId,
                    IHttpContextService httpContextService, ISubscriberIdProvider subscribers,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    await using var stream = file.OpenReadStream();
                    var result = await sender.Send(new UploadProrationProofCommand(
                        subscriptionId,
                        paymentId,
                        subscriberId,
                        stream,
                        file.FileName,
                        file.ContentType,
                        file.Length,
                        paymentMethodId), ct);

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("upload")
            .WithName("UploadProrationProof")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<UploadPaymentProofResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
