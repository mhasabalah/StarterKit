using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.E2ETests.Infrastructure;
using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Dtos;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.Queries;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Catalog;

/// <summary>
/// Catalog's product search on the search index, in the whole application: the real Hangfire server builds the
/// <c>catalog.products</c> index, a product published over HTTP reaches the index through Catalog's outbox and the bus,
/// and the public search and suggestions answer from it.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ProductSearchIndexTests(E2ETestFixture fixture)
{
    private const string Products = "/api/v1/products";
    private const string Index = "/api/v1/search/indexes/catalog.products";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data);
    private sealed record CreatedResponse(Guid Id);
    private sealed record IndexState(string Key, DateTime? LastBuiltAt);

    private HttpClient Admin()
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    [Fact]
    public async Task A_published_product_is_found_through_the_index_with_arabic_normalization_and_suggestions()
    {
        var admin = Admin();
        await WaitForIndexAsync(admin);

        var token = "q" + Guid.NewGuid().ToString("N")[..10].Replace('0', 'x').Replace('1', 'y');
        var id = await CreatePublishedAsync(admin, $"Floor lamp {token}", $"مِصباحٌ أرضي {token}");
        var anonymous = fixture.CreateAnonymousClient();

        // The change travels outbox → bus → Search consumer: poll until the index answers with it.
        var result = await PollAsync(async () =>
        {
            var page = await ReadAsync<SearchProductsResult>(
                await anonymous.GetAsync($"{Products}/search?searchTerm=lamps%20{token}&highlight=true&facets=Type"));
            return page.Match == ProductSearchMatch.Exact && page.Result!.Any(p => p.Id == id) ? page : null;
        });

        result.Highlights[id]["nameEn"].Should().Contain("<mark>lamp</mark>", "\"lamps\" matches \"lamp\" by stemming");
        result.Facets.Should().ContainSingle(f => f.Facet == ProductFacet.Type);

        var arabic = await ReadAsync<SearchProductsResult>(
            await anonymous.GetAsync($"{Products}/search?searchTerm={Uri.EscapeDataString("مصباح " + token)}"));
        arabic.Result!.Select(p => p.Id).Should().Contain(id, "diacritics and letter forms are normalized");

        var suggestions = await ReadAsync<List<ProductSuggestionDto>>(
            await anonymous.GetAsync($"{Products}/suggest?q={token}%20flo"));
        suggestions.Should().ContainSingle(s => s.Id == id).Which.Highlight.Should().Contain("<mark>");
    }

    [Fact]
    public async Task Suggestions_validate_their_input()
    {
        var anonymous = fixture.CreateAnonymousClient();

        (await anonymous.GetAsync($"{Products}/suggest?q=")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anonymous.GetAsync($"{Products}/suggest?q=lamp&limit=100")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anonymous.GetAsync($"{Products}/search?searchTerm=lamp&facets=Nope")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The reconcile job (triggered at startup) builds the index on the real Hangfire server.</summary>
    private static async Task WaitForIndexAsync(HttpClient admin) =>
        await PollAsync(async () =>
        {
            var index = await ReadAsync<IndexState>(await admin.GetAsync(Index));
            return index.LastBuiltAt is not null ? index : null;
        });

    private static async Task<Guid> CreatePublishedAsync(HttpClient admin, string nameEn, string nameAr)
    {
        var payload = TestData.ValidCreateProductPayload(nameEn: nameEn);
        payload["name"] = new { en = nameEn, ar = nameAr };
        var id = (await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync(Products, payload), HttpStatusCode.Created)).Id;

        var submitted = await ReadAsync<ProductStatusChangedResult>(await admin.PostAsJsonAsync($"{Products}/{id}/submit-for-review", new { }));
        var approved = await ReadAsync<ProductStatusChangedResult>(await admin.PostAsJsonAsync(
            $"{Products}/{id}/approve", new { reviewNotes = "ok", version = submitted.Version }));
        await ReadAsync<ProductStatusChangedResult>(await admin.PostAsJsonAsync($"{Products}/{id}/publish", new { version = approved.Version }));
        return id;
    }

    private static async Task<T> PollAsync<T>(Func<Task<T?>> attempt) where T : class
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (await attempt() is { } value)
                return value;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Not reached within {Timeout}.");
            await Task.Delay(250);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Envelope<T>>(Json))!.Data!;
    }
}
