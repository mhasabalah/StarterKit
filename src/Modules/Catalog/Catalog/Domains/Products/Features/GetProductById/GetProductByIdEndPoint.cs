namespace Catalog.Domains.Products.Features.GetProductById;

public class GetProductByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/{id:guid}",
                async (Guid id, bool? incrementView, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetProductByIdQuery(id, incrementView ?? true), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result.Product));
                })
            .WithName("GetProductById")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get Product By ID")
            .WithDescription(
                "Published product detail with the full gallery (owners also see their own unpublished products). " +
                "Increments the view counter unless `incrementView=false` or the caller is the owner. Public.")
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
