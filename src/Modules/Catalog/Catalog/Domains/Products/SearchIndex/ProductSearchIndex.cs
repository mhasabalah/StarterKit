using Search.Contracts.Indexing;
using Search.Contracts.Querying;

namespace Catalog.Domains.Products.SearchIndex;

/// <summary>
/// The products search index (<c>catalog.products</c>): names in both languages and the SKU weigh most and feed
/// suggestions, the description less; the catalogue filters are typed filter fields. Every product that is not
/// soft-deleted is indexed (any status): public searches add <c>status = Published</c> themselves.
/// Tag and category NAMES are not indexed, only their ids: renaming a tag never re-indexes its products.
/// </summary>
public static class ProductSearchIndex
{
    public const string Key = "catalog.products";

    public static readonly SearchIndexDefinition Definition = new()
    {
        Key = Key,
        Name = LocalizedText.Create("Products", "المنتجات"),
        TextFields =
        [
            new SearchTextField(Fields.NameEn, SearchWeight.Highest, SearchAnalyzer.English) { Suggest = true, Highlight = true },
            new SearchTextField(Fields.NameAr, SearchWeight.Highest, SearchAnalyzer.Arabic) { Suggest = true, Highlight = true },
            new SearchTextField(Fields.Sku, SearchWeight.Highest, SearchAnalyzer.Simple) { Suggest = true },
            new SearchTextField(Fields.Description, SearchWeight.Medium, SearchAnalyzer.Bilingual)
            {
                Highlight = true,
                MaxLength = Product.DescriptionMaxLength
            }
        ],
        FilterFields =
        [
            new SearchFilterField(Fields.Status, SearchFieldType.Keyword) { Facetable = true },
            new SearchFilterField(Fields.Type, SearchFieldType.Keyword) { Facetable = true },
            new SearchFilterField(Fields.Currency, SearchFieldType.Keyword) { Facetable = true },
            new SearchFilterField(Fields.CategoryId, SearchFieldType.Keyword) { Facetable = true },
            new SearchFilterField(Fields.TagIds, SearchFieldType.KeywordList) { Facetable = true },
            new SearchFilterField(Fields.AttributeTypeIds, SearchFieldType.KeywordList),
            new SearchFilterField(Fields.OwnerId, SearchFieldType.Keyword),
            new SearchFilterField(Fields.IsFeatured, SearchFieldType.Boolean) { Facetable = true },
            new SearchFilterField(Fields.Price, SearchFieldType.Number) { Sortable = true },
            new SearchFilterField(Fields.CreatedAt, SearchFieldType.Date) { Sortable = true },
            new SearchFilterField(Fields.PublishedAt, SearchFieldType.Date) { Sortable = true }
        ],
        DefaultSort = new SearchSort(Fields.CreatedAt, Descending: true)
    };

    /// <summary>The field names: stored with every document, so a rename is a definition change (full rebuild).</summary>
    public static class Fields
    {
        public const string NameEn = "name_en";
        public const string NameAr = "name_ar";
        public const string Sku = "sku";
        public const string Description = "description";
        public const string Status = "status";
        public const string Type = "type";
        public const string Currency = "currency";
        public const string CategoryId = "category_id";
        public const string TagIds = "tag_ids";
        public const string AttributeTypeIds = "attribute_type_ids";
        public const string OwnerId = "owner_id";
        public const string IsFeatured = "is_featured";
        public const string Price = "price";
        public const string CreatedAt = "created_at";
        public const string PublishedAt = "published_at";
    }
}
