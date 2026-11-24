using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Shared.Contracts.Localization;

namespace Search.Tests;

/// <summary>A small, valid index definition that tests change one property at a time.</summary>
public static class TestIndexes
{
    public static SearchIndexDefinition Articles(string key = "test.articles") => new()
    {
        Key = key,
        Name = LocalizedText.Create("Articles", "المقالات"),
        TextFields =
        [
            new SearchTextField("title", SearchWeight.Highest, SearchAnalyzer.Bilingual) { Suggest = true, Highlight = true },
            new SearchTextField("body", SearchWeight.Low, SearchAnalyzer.English) { MaxLength = 2000 }
        ],
        FilterFields =
        [
            new SearchFilterField("status", SearchFieldType.Keyword) { Facetable = true },
            new SearchFilterField("tag_ids", SearchFieldType.KeywordList) { Facetable = true },
            new SearchFilterField("price", SearchFieldType.Number) { Sortable = true },
            new SearchFilterField("published_at", SearchFieldType.Date) { Sortable = true },
            new SearchFilterField("featured", SearchFieldType.Boolean) { Facetable = true }
        ],
        DefaultSort = new SearchSort("published_at", Descending: true)
    };
}
