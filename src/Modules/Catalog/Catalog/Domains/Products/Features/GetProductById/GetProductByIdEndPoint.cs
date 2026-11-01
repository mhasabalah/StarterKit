using Catalog.Contracts.Products.Queries;

namespace Catalog.Domains.Products.Features.GetProductById;

public class GetProductByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetProductByIdQuery(id), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result.Product));
                })
            .WithName("GetProductById")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get Product By ID")
            .WithDescription("Retrieve a single product, including its concurrency `version`. Public.")
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
