using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Shared.Contracts.Localization;
using Shared.Core.MultiTenancy;

namespace Search.IntegrationTests.TestIndex;

/// <summary>
/// A test index that every engine is exercised with (no business module needed): bilingual titles, English bodies,
/// codes, and one filter field of each type.
/// </summary>
public static class TestArticles
{
    public const string Key = "test.articles";

    public static readonly SearchIndexDefinition Definition = new()
    {
        Key = Key,
        Name = LocalizedText.Create("Articles", "المقالات"),
        TextFields =
        [
            new SearchTextField("title", SearchWeight.Highest, SearchAnalyzer.Bilingual) { Suggest = true, Highlight = true },
            new SearchTextField("code", SearchWeight.Highest, SearchAnalyzer.Simple) { Suggest = true },
            new SearchTextField("body", SearchWeight.Low, SearchAnalyzer.English) { Highlight = true, MaxLength = 2000 }
        ],
        FilterFields =
        [
            new SearchFilterField("status", SearchFieldType.Keyword) { Facetable = true, Sortable = true },
            new SearchFilterField("tag_ids", SearchFieldType.KeywordList) { Facetable = true },
            new SearchFilterField("price", SearchFieldType.Number) { Sortable = true },
            new SearchFilterField("published_at", SearchFieldType.Date) { Sortable = true },
            new SearchFilterField("featured", SearchFieldType.Boolean) { Facetable = true }
        ],
        DefaultSort = new SearchSort("published_at", Descending: true)
    };

    public static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>An article document; omitted values are left out of the document.</summary>
    public static SearchDocument Article(
        string id, string? title, string? body = null, string? code = null, string? status = "Published",
        string[]? tags = null, decimal? price = null, int? publishedDay = null, bool? featured = null,
        Guid? branch = null, long version = 1) =>
        SearchDocument.For(id, version)
            .InBranch(branch)
            .Text("title", title).Text("body", body).Text("code", code)
            .Keyword("status", status)
            .Keywords("tag_ids", tags)
            .Number("price", price)
            .Date("published_at", publishedDay is { } day ? Epoch.AddDays(day) : null)
            .Bool("featured", featured)
            .Build();
}

/// <summary>Articles per tenant, held in memory (the "owning module" of the test index).</summary>
public sealed class ArticleStore
{
    private readonly ConcurrentDictionary<(Guid Tenant, string Id), SearchDocument> _articles = new();

    public void Put(Guid tenant, SearchDocument document) => _articles[(tenant, document.Id)] = document;

    public void Remove(Guid tenant, string id) => _articles.TryRemove((tenant, id), out _);

    public IEnumerable<SearchDocument> Of(Guid tenant) =>
        _articles.Where(a => a.Key.Tenant == tenant).Select(a => a.Value).OrderBy(d => d.Id, StringComparer.Ordinal);
}

/// <summary>The test index's source: the current tenant's articles from <see cref="ArticleStore"/>.</summary>
public sealed class InMemoryArticleSource(ArticleStore store, ITenantContext tenant) : ISearchDocumentSource
{
    private Guid Tenant => tenant.TenantId ?? throw new InvalidOperationException("No tenant in scope.");

    public Task<IReadOnlyList<SearchDocument>> GetAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SearchDocument>>(store.Of(Tenant).Where(d => ids.Contains(d.Id)).ToList());

    public async IAsyncEnumerable<SearchDocument> StreamAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var document in store.Of(Tenant))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return document;
        }
    }
}
