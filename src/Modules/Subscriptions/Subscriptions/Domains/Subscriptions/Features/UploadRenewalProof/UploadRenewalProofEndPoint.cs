using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.UploadRenewalProof;

public class UploadRenewalProofEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{subscriptionId:guid}/renewal/proof",
                async (Guid subscriptionId,
                    [FromForm] IFormFile file,
                    [FromForm] Guid? paymentId,
                    [FromForm] Guid paymentMethodId,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await using var stream = file.OpenReadStream();
                    var result = await sender.Send(new UploadRenewalProofCommand(
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
            .WithName("UploadRenewalProof")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<UploadPaymentProofResult>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
