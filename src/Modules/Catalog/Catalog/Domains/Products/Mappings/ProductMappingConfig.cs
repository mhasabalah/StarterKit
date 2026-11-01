namespace Catalog.Domains.Products.Mappings;

/// <summary>
/// Mapster config — picked up by <c>AddCustomMapster(CatalogRoot.AssemblyName)</c>.
/// <see cref="ProductDto"/> maps by convention; this registration exists so the
/// projection used by queries (<c>ProjectToType&lt;ProductDto&gt;</c>) is compiled once
/// and to show where explicit member mappings belong.
/// </summary>
public class ProductMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductDto>();
    }
}
