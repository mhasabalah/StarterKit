using Search.Contracts.Indexing;
using Search.Contracts.Indexing.IntegrationEvents;
using Search.Contracts.Querying;

namespace Search.Tests.Contracts;

public sealed class SearchDocumentBuilderTests
{
    [Fact]
    public void Builds_text_filters_branch_and_version()
    {
        var id = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var tag = Guid.NewGuid();
        var at = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

        var document = SearchDocument.For(id, 7).InBranch(branch)
            .Text("title", "Lamp")
            .Keyword("status", "Published").Keyword("owner_id", id)
            .Keywords("tag_ids", [tag, tag])
            .Number("price", 10m).Date("published_at", at).Bool("featured", false)
            .Build();

        document.Id.Should().Be(id.ToString());
        document.Version.Should().Be(7);
        document.BranchId.Should().Be(branch);
        document.Text.Should().Equal(new Dictionary<string, string> { ["title"] = "Lamp" });
        document.Filters["status"].Should().Be(new KeywordValue("Published"));
        document.Filters["owner_id"].Should().Be(new KeywordValue(id.ToString()));
        document.Filters["tag_ids"].Should().Be(new KeywordListValue([tag.ToString()]), "duplicates are removed");
        document.Filters["price"].Should().Be(new NumberValue(10m));
        document.Filters["published_at"].Should().Be(new DateValue(at));
        document.Filters["featured"].Should().Be(new BooleanValue(false));
    }

    [Fact]
    public void Null_and_blank_values_leave_the_field_out()
    {
        var document = SearchDocument.For("p1", 1)
            .Text("title", "  ").Text("body", null)
            .Keyword("status", (string?)null).Keyword("owner_id", (Guid?)null).Keywords("tag_ids", (string[]?)null)
            .Number("price", null).Date("published_at", null).Bool("featured", null)
            .Build();

        document.Text.Should().BeEmpty();
        document.Filters.Should().BeEmpty();
        document.BranchId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/id")]
    [InlineData("ünïcode")]
    public void An_id_outside_the_safe_charset_is_rejected(string id) =>
        FluentActions.Invoking(() => SearchDocument.For(id, 1)).Should().Throw<ArgumentException>();

    [Fact]
    public void An_id_longer_than_the_limit_is_rejected() =>
        FluentActions.Invoking(() => SearchDocument.For(new string('a', SearchDocument.IdMaxLength + 1), 1))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void A_negative_version_is_rejected() =>
        FluentActions.Invoking(() => SearchDocument.For("p1", -1)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void A_field_set_twice_is_rejected() =>
        FluentActions.Invoking(() => SearchDocument.For("p1", 1).Text("title", "a").Keyword("title", "b"))
            .Should().Throw<ArgumentException>().WithMessage("*'title' is set twice*");

    [Fact]
    public void A_date_that_is_not_utc_is_rejected() =>
        FluentActions.Invoking(() => SearchDocument.For("p1", 1).Date("published_at", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)))
            .Should().Throw<ArgumentException>().WithMessage("*must be UTC*");

    [Fact]
    public void Keyword_lists_compare_by_their_values() =>
        new KeywordListValue(["a", "b"]).Should().Be(new KeywordListValue(["a", "b"]))
            .And.NotBe(new KeywordListValue(["b", "a"]));

    [Fact]
    public void Each_value_fits_only_its_field_type()
    {
        new KeywordValue("x").Fits(SearchFieldType.Keyword).Should().BeTrue();
        new KeywordValue("x").Fits(SearchFieldType.KeywordList).Should().BeFalse();
        new NumberValue(1).Fits(SearchFieldType.Number).Should().BeTrue();
        new DateValue(DateTime.UtcNow).Fits(SearchFieldType.Date).Should().BeTrue();
        new BooleanValue(true).Fits(SearchFieldType.Boolean).Should().BeTrue();
    }
}

public sealed class SearchDocumentsChangedIntegrationEventTests
{
    [Fact]
    public void Carries_the_distinct_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var e = SearchDocumentsChangedIntegrationEvent.For("catalog.products", a, b, a);

        e.IndexKey.Should().Be("catalog.products");
        e.DocumentIds.Should().Equal(a.ToString(), b.ToString());
    }

    [Fact]
    public void Needs_at_least_one_id() =>
        FluentActions.Invoking(() => SearchDocumentsChangedIntegrationEvent.For("test.articles", Array.Empty<string>()))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Carries_at_most_the_batch_limit() =>
        FluentActions.Invoking(() => SearchDocumentsChangedIntegrationEvent.For(
                "test.articles", Enumerable.Range(0, SearchDocumentsChangedIntegrationEvent.MaxDocuments + 1).Select(i => $"d{i}")))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*split larger changes*");

    [Fact]
    public void Rejects_an_invalid_id() =>
        FluentActions.Invoking(() => SearchDocumentsChangedIntegrationEvent.For("test.articles", "ok", "not ok"))
            .Should().Throw<ArgumentException>().WithMessage("*'not ok'*");
}

public sealed class SearchDocumentsResultTests
{
    [Fact]
    public void An_unavailable_result_has_no_hits_and_keeps_the_page()
    {
        var result = SearchDocumentsResult.Unavailable(SearchStatus.IndexNotReady, 2, 20);

        result.Status.Should().Be(SearchStatus.IndexNotReady);
        result.Hits.Should().BeEmpty();
        result.Total.Should().Be(0);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public void Ok_is_not_an_unavailable_status() =>
        FluentActions.Invoking(() => SearchDocumentsResult.Unavailable(SearchStatus.Ok, 1, 20))
            .Should().Throw<ArgumentOutOfRangeException>();
}
