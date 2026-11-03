using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Shared.Contracts.CQRS;
using Shared.Contracts.Enums;
using Shared.Core.Authorization;
using Shared.Core.Extensions;
using Shared.Core.Pagination;

namespace Shared.Core.Tests.Paging;

public sealed class PagingTests
{
    private sealed record Row(Guid Id, string Name, int Rank, DateTime CreatedAt, List<string> Tags);

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Same instances on every call so record equality (which compares the Tags list by reference) holds.
    private static readonly Row[] Source = Enumerable.Range(1, 500)
        .Select(i => new Row(new Guid(i, 0, 0, new byte[8]), $"n{i}", i % 3, T0.AddMinutes(i % 4), []))
        .ToArray();

    private static IQueryable<Row> Rows(int count) => Source.Take(count)
        .Reverse() // source order != any sort order
        .AsQueryable();

    [Theory]
    [InlineData(null, null, 1, PagingOptions.DefaultPageSizeValue)]
    [InlineData(0, 0, 1, PagingOptions.DefaultPageSizeValue)]
    [InlineData(-3, -1, 1, PagingOptions.DefaultPageSizeValue)] // -1 is no longer "all rows"
    [InlineData(2, 1, 2, 1)]
    [InlineData(1, 100, 1, 100)]
    [InlineData(1, 101, 1, 100)]
    [InlineData(1, int.MaxValue, 1, 100)]
    [InlineData(int.MaxValue, 50, int.MaxValue, 50)]
    public void Normalize_clamps_page_and_size(int? page, int? size, int expectedPage, int expectedSize)
    {
        PagingOptions.Normalize(page, size).Should().Be((expectedPage, expectedSize));
    }

    [Fact]
    public void Skip_saturates_instead_of_overflowing()
    {
        PagingOptions.Skip(int.MaxValue, 100).Should().Be(int.MaxValue);
        PagingOptions.Skip(1, 100).Should().Be(0);
        PagingOptions.Skip(3, 10).Should().Be(20);
    }

    [Fact]
    public async Task Minus_one_page_size_no_longer_returns_the_whole_table()
    {
        var page = await Rows(500).PagedResult(1, -1, CancellationToken.None);

        page.Result.Should().HaveCount(PagingOptions.DefaultPageSizeValue);
        page.PageInfoResult.PageSize.Should().Be(PagingOptions.DefaultPageSizeValue);
        page.PageInfoResult.TotalItems.Should().Be(500);
    }

    [Fact]
    public async Task Huge_page_size_is_capped_at_max_page_size()
    {
        var page = await Rows(500).PagedResult((int?)1, 1_000_000, CancellationToken.None);

        page.Result.Should().HaveCount(PagingOptions.MaxPageSize);
        page.PageInfoResult.PageSize.Should().Be(PagingOptions.MaxPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_page_index_returns_the_first_page_instead_of_failing(int pageIndex)
    {
        var page = await Rows(30).OrderByDynamic("Id", SortBy.Ascending).PagedResult(pageIndex, 10, CancellationToken.None);

        page.PageInfoResult.CurrentPage.Should().Be(1);
        page.Result!.First().Name.Should().Be("n1");
    }

    [Fact]
    public async Task Huge_page_index_returns_an_empty_page()
    {
        var page = await Rows(30).PagedResult(int.MaxValue, 10, CancellationToken.None);

        page.Result.Should().BeEmpty();
        page.PageInfoResult.TotalItems.Should().Be(30);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("doesNotExist")]
    [InlineData("Tags")] // a collection: not sortable → treated as unknown, not a 500
    public void Unknown_sort_column_falls_back_to_CreatedAt_then_Id(string? column)
    {
        var sorted = Rows(20).OrderByDynamic(column, SortBy.Ascending).ToList();

        var expected = Rows(20).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
        sorted.Should().Equal(expected);
    }

    [Fact]
    public void Known_sort_column_is_case_insensitive_and_ties_are_broken_by_id()
    {
        var sorted = Rows(20).OrderByDynamic("rank", SortBy.Descending).ToList();

        var expected = Rows(20).OrderByDescending(r => r.Rank).ThenByDescending(r => r.Id).ToList();
        sorted.Should().Equal(expected);
    }

    [Fact]
    public void Explicit_default_sort_property_is_used_for_unknown_columns()
    {
        var sorted = Rows(20).OrderByDynamic("nope", SortBy.Ascending, defaultProperty: "Name").ToList();

        sorted.Should().Equal(Rows(20).OrderBy(r => r.Name).ThenBy(r => r.Id).ToList());
    }

    [Fact]
    public void Existing_order_is_kept_for_unknown_columns_and_gets_an_id_tie_breaker()
    {
        var sorted = Rows(20).OrderBy(r => r.Rank).OrderByDynamic(null, SortBy.Ascending).ToList();

        sorted.Should().Equal(Rows(20).OrderBy(r => r.Rank).ThenBy(r => r.Id).ToList());
    }

    [Fact]
    public void Ordering_is_deterministic_across_runs()
    {
        var first = Rows(50).OrderByDynamic("Rank", SortBy.Ascending).Select(r => r.Id).ToList();
        var second = Rows(50).OrderByDynamic("Rank", SortBy.Ascending).Select(r => r.Id).ToList();

        first.Should().Equal(second);
    }

    private sealed record TestListQuery : BaseQuery;

    [Theory]
    [InlineData(1, 10, null, true)]
    [InlineData(null, null, null, true)]
    [InlineData(1, 100, "createdAt", true)]
    [InlineData(0, 10, null, false)]
    [InlineData(-1, 10, null, false)]
    [InlineData(1, 0, null, false)]
    [InlineData(1, -1, null, false)]
    [InlineData(1, 101, null, false)]
    [InlineData(1, 10, "name; drop table", false)]
    public void BaseQueryValidator_rejects_out_of_range_input(int? page, int? size, string? sortColumn, bool valid)
    {
        var query = new TestListQuery { PageNumber = page, PageSize = size, SortColumn = sortColumn };

        new BaseQueryValidator<TestListQuery>().Validate(query).IsValid.Should().Be(valid);
    }

    [Fact]
    public void PagingOptions_rejects_invalid_configuration()
    {
        var act = () => PagingOptions.Configure(maxPageSize: 0, defaultPageSize: 10, anonymousMaxPageSize: 10);
        act.Should().Throw<InvalidOperationException>();

        var act2 = () => PagingOptions.Configure(maxPageSize: 50, defaultPageSize: 10, anonymousMaxPageSize: 60);
        act2.Should().Throw<InvalidOperationException>();
    }

    private static async Task<bool> PageGateAllows(string queryString, bool authenticated, int maxAnonymous = 20)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(queryString);
        var user = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u")], "Test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        var requirement = new PageGateRequirement(maxAnonymous);
        var context = new AuthorizationHandlerContext([requirement], user, http);
        await new PageGateAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("?pageNumber=1", true)]
    [InlineData("?pageNumber=1&pageSize=20", true)]
    [InlineData("?pageNumber=1&pageSize=21", false)]
    [InlineData("?pageSize=-1", false)]
    [InlineData("?pageSize=0", false)]
    [InlineData("?pageSize=100000", false)]
    [InlineData("?pageSize=abc", false)]
    [InlineData("?pageSize=10&pageSize=5000", false)]
    [InlineData("?pageNumber=2", false)]
    public async Task PageGate_limits_anonymous_page_and_page_size(string query, bool allowed)
    {
        (await PageGateAllows(query, authenticated: false)).Should().Be(allowed);
    }

    [Theory]
    [InlineData("?pageNumber=5&pageSize=100")]
    [InlineData("?pageSize=-1")]
    public async Task PageGate_lets_authenticated_users_through(string query)
    {
        // Size is still clamped by the paging helpers.
        (await PageGateAllows(query, authenticated: true)).Should().BeTrue();
    }
}
