using Microsoft.Extensions.Options;
using Search.Engine.Text;

namespace Search.Domains.Analytics.Services;

/// <summary>
/// Decides what a search contributes to the analytics and hands it to the <see cref="SearchAnalyticsBuffer"/>. Only
/// what a person typed and submitted counts: full-mode searches with text, first page (not autocomplete keystrokes, not
/// paging, not browsing by filters). Zero-result terms are scrubbed by <see cref="SearchQueryScrubber"/>. Off with
/// <c>Search:Analytics:Enabled=false</c>.
/// </summary>
public sealed class SearchAnalyticsRecorder(SearchAnalyticsBuffer buffer, IOptions<SearchOptions> options, TimeProvider time)
{
    public void Record(Guid tenantId, SearchDocumentsQuery query, ParsedSearchQuery text, SearchDocumentsResult result)
    {
        var settings = options.Value.Analytics;
        if (!settings.Enabled || query.Mode != SearchMode.Full || query.Page != 1 || text.IsEmpty)
            return;

        var outcome = result switch
        {
            { Status: not SearchStatus.Ok } => SearchOutcome.Unanswered,
            { Total: 0 } => SearchOutcome.ZeroResults,
            { MatchMode: SearchMatchMode.Fuzzy } => SearchOutcome.FuzzyRescue,
            _ => SearchOutcome.Found
        };

        var term = outcome == SearchOutcome.ZeroResults ? SearchQueryScrubber.Scrub(query.Text, settings.MaxQueryLength) : null;
        buffer.Record(tenantId, query.IndexKey, outcome, term, time.GetUtcNow().UtcDateTime);
    }
}
