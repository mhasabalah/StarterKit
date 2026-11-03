namespace Catalog.Domains.Products.Features.UploadProductImage;

public class UploadProductImageEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/images/upload",
                async ([FromRoute] Guid id, IFormFile file, [FromForm] string? caption, [FromForm] bool? isPrimary,
                    ISender sender, CancellationToken cancellationToken) =>
                {
                    await using var stream = file.OpenReadStream();

                    var result = await sender.Send(new UploadProductImageCommand(
                        id,
                        file.FileName,
                        file.ContentType,
                        file.Length,
                        stream,
                        caption,
                        isPrimary ?? false), cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("UploadProductImage")
            .WithTags("Product Images")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .DisableAntiforgery()
            .Produces<ApiResponse<UploadProductImageResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Upload Product Image")
            .WithDescription(
                "Multipart upload (`file`, optional `caption`, `isPrimary`) into the product gallery. " +
                "The first image becomes primary automatically. Owner (editable status) or admin.")
            .RequireAuthorization()
            .RequireRateLimiting("upload")
            .HasApiVersion(1.0);
    }
}
