using Search.Domains.Indexes.Models;

namespace Search.Tests.Indexes;

public sealed class SearchIndexStateTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);

    [Fact]
    public void A_new_index_is_not_built_and_cannot_answer()
    {
        var state = SearchIndexState.Create("test.articles", Now);

        state.Status.Should().Be(SearchIndexStatus.NotBuilt);
        state.CanAnswer.Should().BeFalse();
        state.WriteStamp.Should().Be(0);
        state.IsStale("abc").Should().BeTrue();
        state.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void A_completed_build_answers_and_becomes_the_live_stamp()
    {
        var state = SearchIndexState.Create("test.articles", Now);

        state.TryStartBuild(42, Now, Lease).Should().BeTrue();
        state.WriteStamp.Should().Be(42, "live writes during a build carry the build's stamp");
        state.CompleteBuild(42, 10, "fp1", Now.AddMinutes(1));

        state.Status.Should().Be(SearchIndexStatus.Ready);
        state.CanAnswer.Should().BeTrue();
        state.ActiveStamp.Should().Be(42);
        state.BuildingStamp.Should().BeNull();
        state.LeaseUntil.Should().BeNull();
        state.DocumentCount.Should().Be(10);
        state.IsStale("fp1").Should().BeFalse();
        state.IsStale("fp2").Should().BeTrue();
    }

    [Fact]
    public void Only_one_worker_holds_the_lease_until_it_expires()
    {
        var state = SearchIndexState.Create("test.articles", Now);
        state.TryStartBuild(1, Now, Lease).Should().BeTrue();

        state.TryStartBuild(2, Now.AddMinutes(10), Lease).Should().BeFalse();
        state.IsLeased(Now.AddMinutes(10)).Should().BeTrue();

        state.TryStartBuild(3, Now.AddMinutes(31), Lease).Should().BeTrue("an expired lease is taken over");
        state.BuildingStamp.Should().Be(3);
        state.ExtendLease(1, Now.AddMinutes(32), Lease).Should().BeFalse("the first worker lost its lease");
        FluentActions.Invoking(() => state.CompleteBuild(1, 0, "fp", Now)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Extending_the_lease_keeps_it()
    {
        var state = SearchIndexState.Create("test.articles", Now);
        state.TryStartBuild(1, Now, Lease);

        state.ExtendLease(1, Now.AddMinutes(20), Lease).Should().BeTrue();

        state.LeaseUntil.Should().Be(Now.AddMinutes(50));
    }

    [Fact]
    public void A_failed_first_build_fails_the_index()
    {
        var state = SearchIndexState.Create("test.articles", Now);
        state.TryStartBuild(1, Now, Lease);

        state.FailBuild(1, "source_failed");

        state.Status.Should().Be(SearchIndexStatus.Failed);
        state.CanAnswer.Should().BeFalse();
        state.LastErrorCode.Should().Be("source_failed");
        state.TryStartBuild(2, Now, Lease).Should().BeTrue("a failed index can be rebuilt at once");
    }

    [Fact]
    public void A_failed_rebuild_keeps_the_previous_build_answering()
    {
        var state = SearchIndexState.Create("test.articles", Now);
        state.TryStartBuild(1, Now, Lease);
        state.CompleteBuild(1, 5, "fp1", Now);
        state.TryStartBuild(2, Now.AddDays(1), Lease);

        state.FailBuild(2, new string('x', 300));

        state.Status.Should().Be(SearchIndexStatus.Ready);
        state.CanAnswer.Should().BeTrue();
        state.ActiveStamp.Should().Be(1);
        state.WriteStamp.Should().Be(1);
        state.LastErrorCode.Should().HaveLength(SearchIndexState.ErrorCodeMaxLength);

        state.TryStartBuild(3, Now.AddDays(1), Lease);
        state.CompleteBuild(3, 6, "fp2", Now.AddDays(1));
        state.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void Drift_numbers_are_recorded()
    {
        var state = SearchIndexState.Create("test.articles", Now);

        state.RecordDrift(1, 2, 3, Now);

        (state.DriftMissing, state.DriftStale, state.DriftOrphaned, state.DriftCheckedAt).Should().Be((1, 2, 3, Now));
        FluentActions.Invoking(() => state.RecordDrift(-1, 0, 0, Now)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
