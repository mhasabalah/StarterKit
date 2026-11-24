using Catalog.Contracts.Products.Queries;
using Catalog.Domains.Products.SearchIndex;
using Search.Contracts.Indexing.IntegrationEvents;
using Search.Contracts.Querying;
using Shared.Contracts.CQRS;
using Shared.Contracts.Enums;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Search;

/// <summary>Every change of a product asks the search index to re-read it: once per save, never more.</summary>
public sealed class ProductSearchEventTests
{
    private static IReadOnlyList<SearchDocumentsChangedIntegrationEvent> SearchEvents(Product product) =>
        product.IntegrationEvents.OfType<SearchDocumentsChangedIntegrationEvent>().ToList();

    [Fact]
    public void Create_raises_one_event_for_the_product_in_the_products_index()
    {
        var product = Product.Create(Name(), null, "SKU-1", ProductType.Physical, Price(), OwnerId);

        var evt = SearchEvents(product).Should().ContainSingle().Which;
        evt.IndexKey.Should().Be(ProductSearchIndex.Key);
        evt.DocumentIds.Should().Equal(product.Id.ToString());
    }

    [Fact]
    public void Several_changes_before_one_save_raise_one_event()
    {
        var product = ProductIn(ProductStatus.Published);
        product.ClearIntegrationEvents();

        product.Update(Name("Renamed"), "desc", "SKU-2", ProductType.Digital, Price(20m), null);
        product.AddTag(Guid.NewGuid());
        product.AddAttribute(Guid.NewGuid(), "red");
        product.SetFeatured(true);

        SearchEvents(product).Should().ContainSingle();
    }

    public static TheoryData<Action<Product>> Mutations => new()
    {
        p => p.Update(Name("New"), null, "SKU-9", ProductType.Physical, Price(), null),
        p => p.AddTag(Guid.NewGuid()),
        p => p.Archive(),
        p => p.SetFeatured(true),
        p => p.Delete()
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Each_save_raises_its_own_event(Action<Product> mutate)
    {
        var product = ProductIn(ProductStatus.Published);
        product.ClearIntegrationEvents(); // what the outbox does on save

        mutate(product);

        SearchEvents(product).Should().ContainSingle();
    }

    [Fact]
    public void Restore_and_purge_raise_an_event_too()
    {
        var product = NewProduct();
        product.Delete();
        product.ClearIntegrationEvents();
        product.Restore();
        SearchEvents(product).Should().ContainSingle();

        product.Delete();
        product.ClearIntegrationEvents();
        product.MarkPurged();
        SearchEvents(product).Should().ContainSingle();
    }

    [Fact]
    public void Publishing_raises_the_published_event_and_one_search_event()
    {
        var product = ProductIn(ProductStatus.Approved);
        product.ClearIntegrationEvents();

        product.Publish();

        product.IntegrationEvents.Should().HaveCount(2);
        SearchEvents(product).Should().ContainSingle();
    }
}

public sealed class ProductSearchMappingTests
{
    private static SearchProductsQuery Query() => new() { SearchTerm = "lamp" };

    [Fact]
    public void Every_filter_maps_to_its_index_field_with_the_public_status()
    {
        var category = Guid.NewGuid();
        var tag = Guid.NewGuid();
        var attribute = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var query = Query() with
        {
            Type = ProductType.Digital,
            Currency = Currency.EUR,
            MinPrice = 5,
            MaxPrice = 50,
            TagIds = [tag, tag],
            AttributeTypeIds = [attribute],
            OwnerId = owner,
            IsFeatured = true
        };

        ProductSearchMapping.TryMapFilters(query, [category], out var filters).Should().BeTrue();

        filters.Should().BeEquivalentTo(new SearchFilter[]
        {
            new KeywordFilter("status", "Published"),
            new KeywordFilter("type", "Digital"),
            new KeywordFilter("currency", "EUR"),
            new KeywordFilter("owner_id", owner.ToString()),
            new BoolFilter("is_featured", true),
            new NumberRangeFilter("price", 5, 50),
            new KeywordsFilter("category_id", [category.ToString()], SearchMatch.Any),
            new KeywordsFilter("tag_ids", [tag.ToString()], SearchMatch.All),
            new KeywordsFilter("attribute_type_ids", [attribute.ToString()], SearchMatch.All)
        }, o => o.RespectingRuntimeTypes());
    }

    [Fact]
    public void Too_many_ids_for_one_filter_decline_the_index() =>
        ProductSearchMapping.TryMapFilters(Query(),
                Enumerable.Range(0, ProductSearchMapping.MaxFilterValues + 1).Select(_ => Guid.NewGuid()).ToList(), out _)
            .Should().BeFalse();

    [Theory]
    [InlineData(null, true, null, false)]
    [InlineData("price", true, "price", false)]
    [InlineData("CreatedAt", true, "created_at", true)]
    [InlineData("publishedAt", true, "published_at", true)]
    [InlineData("Name", false, null, false)]          // a product column the index cannot sort on: SQL answers
    [InlineData("noSuchColumn", true, null, false)]  // unknown: ignored, as the SQL search does
    public void Sort_columns_map_to_sortable_fields_or_decline(string? column, bool mapped, string? field, bool descending)
    {
        var sort = new BaseSortQuery { SortColumn = column, SortBy = descending ? SortBy.Descending : SortBy.Ascending };
        if (column == "price")
            sort.SortBy = SortBy.Ascending;

        ProductSearchMapping.TryMapSort(sort, out var result).Should().Be(mapped);
        result?.Field.Should().Be(field);
        if (result is not null)
            result.Descending.Should().Be(descending);
    }

    [Fact]
    public void Facets_and_highlights_use_the_api_names()
    {
        ProductSearchMapping.FacetFieldsOf([ProductFacet.Category, ProductFacet.Tag, ProductFacet.Featured, ProductFacet.Tag])
            .Should().Equal("category_id", "tag_ids", "is_featured");

        ProductSearchMapping.ToFacets([new SearchFacet("currency", [new SearchFacetValue("USD", 3)]), new SearchFacet("other", [])])
            .Should().BeEquivalentTo([new { Facet = ProductFacet.Currency, Values = new[] { new { Value = "USD", Count = 3 } } }]);

        ProductSearchMapping.ToHighlights(new Dictionary<string, string> { ["name_ar"] = "x", ["sku"] = "y" })
            .Should().Equal(new Dictionary<string, string> { ["nameAr"] = "x" });
    }
}
