namespace Catalog.Domains.Products.Mappings;

/// <summary>
/// Mapster config, picked up by <c>AddCustomMapster(CatalogRoot.AssemblyName)</c>. Used with
/// <c>ProjectToType&lt;TDto&gt;()</c> where the projection is simple (admin lookups). The raw bilingual
/// name is copied into a fresh <see cref="LocalizedText"/>; the culture-specific <c>DisplayName</c> is filled
/// in memory after the query, because a Mapster config is compiled once and must not capture a culture.
/// </summary>
public class LookupMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Tag, TagDto>()
            .Map(d => d.Name, s => new LocalizedText(s.Name.En, s.Name.Ar))
            .Ignore(d => d.DisplayName);

        config.NewConfig<Category, CategoryDto>()
            .Map(d => d.Name, s => new LocalizedText(s.Name.En, s.Name.Ar))
            .Ignore(d => d.DisplayName);
    }
}
