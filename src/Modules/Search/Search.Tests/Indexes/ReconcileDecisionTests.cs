using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Models;
using Search.Engine;

namespace Search.Tests.Indexes;

public sealed class ReconcileDecisionTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly RegisteredIndex Index = new SearchIndexRegistry([TestIndexes.Articles()]).Find("test.articles")!;

    private static SearchIndexState Built(string fingerprint)
    {
        var state = SearchIndexState.Create(Index.Key, Now.AddDays(-1));
        state.TryStartBuild(1, Now.AddDays(-1), TimeSpan.FromMinutes(30));
        state.CompleteBuild(1, 3, fingerprint, Now.AddDays(-1));
        return state;
    }

    [Fact]
    public void A_never_built_index_needs_a_rebuild() =>
        ReconcileSearchIndexesJob.NeedsRebuild(SearchIndexState.Create(Index.Key, Now), Index, Now).Should().BeTrue();

    [Fact]
    public void An_index_built_from_the_current_definition_does_not() =>
        ReconcileSearchIndexesJob.NeedsRebuild(Built(Index.Fingerprint), Index, Now).Should().BeFalse();

    [Fact]
    public void A_changed_definition_needs_a_rebuild() =>
        ReconcileSearchIndexesJob.NeedsRebuild(Built("an-older-fingerprint"), Index, Now).Should().BeTrue();

    [Fact]
    public void A_rebuild_in_progress_is_left_alone()
    {
        var state = SearchIndexState.Create(Index.Key, Now);
        state.TryStartBuild(2, Now.AddMinutes(-5), TimeSpan.FromMinutes(30));

        ReconcileSearchIndexesJob.NeedsRebuild(state, Index, Now).Should().BeFalse();
        ReconcileSearchIndexesJob.NeedsRebuild(state, Index, Now.AddMinutes(30)).Should().BeTrue("the lease expired");
    }

    [Fact]
    public void A_failed_rebuild_is_retried_at_most_once_an_hour()
    {
        var state = SearchIndexState.Create(Index.Key, Now);
        state.TryStartBuild(2, Now.AddMinutes(-10), TimeSpan.FromMinutes(30));
        state.FailBuild(2, "rebuild_failed");

        ReconcileSearchIndexesJob.NeedsRebuild(state, Index, Now).Should().BeFalse();
        ReconcileSearchIndexesJob.NeedsRebuild(state, Index, Now.AddMinutes(-10) + ReconcileSearchIndexesJob.RetryFailedAfter + TimeSpan.FromSeconds(1))
            .Should().BeTrue();
    }
}
