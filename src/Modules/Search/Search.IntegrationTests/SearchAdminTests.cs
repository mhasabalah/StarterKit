using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Search.Contracts.Indexing;
using Search.Data;
using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Models;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests;

/// <summary>
/// The admin API's commands and queries through the mediator, on the real module: the tenant's index states, rebuild and
/// drift requests (enqueued in the caller's tenant), and the platform's engine view and rebuild in every tenant.
/// </summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class SearchAdminTests(SearchPostgresFixture fixture)
{
    private Task SeedStateAsync(Guid tenant, Action<SearchIndexState> change) =>
        fixture.RunAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            var state = SearchIndexState.Create(Key, DateTime.UtcNow);
            change(state);
            db.IndexStates.Add(state);
            return await db.SaveChangesAsync();
        });

    [Fact]
    public async Task An_index_without_a_state_is_listed_as_not_built_with_its_fields()
    {
        var indexes = await fixture.SendAsync(Guid.NewGuid(), new GetSearchIndexesQuery());

        var index = indexes.Should().ContainSingle(i => i.Key == Key).Which;
        index.Status.Should().Be(SearchIndexStatus.NotBuilt);
        index.UpToDate.Should().BeFalse();
        index.Drift.Should().BeNull();
        index.Name.En.Should().Be(Definition.Name.En);
        index.TextFields.Select(f => f.Name).Should().Equal(Definition.TextFields.Select(f => f.Name));
        index.FilterFields.Should().ContainEquivalentOf(new SearchFilterFieldDto("price", SearchFieldType.Number, false, true));
    }

    [Fact]
    public async Task A_built_index_shows_its_build_and_drift_of_the_callers_tenant_only()
    {
        var tenant = Guid.NewGuid();
        await SeedStateAsync(tenant, state =>
        {
            state.TryStartBuild(1, DateTime.UtcNow, TimeSpan.FromMinutes(5));
            state.CompleteBuild(1, 42, fixture.Index.Fingerprint, DateTime.UtcNow);
            state.RecordDrift(1, 2, 3, DateTime.UtcNow);
        });

        var index = await fixture.SendAsync(tenant, new GetSearchIndexByKeyQuery(Key));

        index.Status.Should().Be(SearchIndexStatus.Ready);
        index.UpToDate.Should().BeTrue();
        index.DocumentCount.Should().Be(42);
        index.LastBuiltAt.Should().NotBeNull();
        index.Drift.Should().BeEquivalentTo(new { Missing = 1, Stale = 2, Orphaned = 3 });

        (await fixture.SendAsync(Guid.NewGuid(), new GetSearchIndexByKeyQuery(Key))).Status
            .Should().Be(SearchIndexStatus.NotBuilt, "another tenant never sees this tenant's state (filter + RLS)");
    }

    [Fact]
    public async Task An_index_built_from_another_definition_is_not_up_to_date()
    {
        var tenant = Guid.NewGuid();
        await SeedStateAsync(tenant, state =>
        {
            state.TryStartBuild(1, DateTime.UtcNow, TimeSpan.FromMinutes(5));
            state.CompleteBuild(1, 1, new string('0', SearchIndexState.FingerprintLength), DateTime.UtcNow);
        });

        (await fixture.SendAsync(tenant, new GetSearchIndexByKeyQuery(Key))).UpToDate.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_index_is_not_found()
    {
        var tenant = Guid.NewGuid();

        await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new GetSearchIndexByKeyQuery("test.removed")))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new RebuildSearchIndexCommand("test.removed")))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new CheckSearchIndexDriftCommand("test.removed")))
            .Should().ThrowAsync<NotFoundException>();
        fixture.Jobs.For(tenant).Should().BeEmpty();
    }

    [Fact]
    public async Task A_rebuild_request_enqueues_a_forced_rebuild_in_the_callers_tenant()
    {
        var tenant = Guid.NewGuid();

        var result = await fixture.SendAsync(tenant, new RebuildSearchIndexCommand(Key));

        result.IndexKey.Should().Be(Key);
        var job = fixture.Jobs.For(tenant).Should().ContainSingle().Which.Job;
        job.Type.Should().Be<RebuildSearchIndexJob>();
        job.Args.Take(2).Should().Equal(Key, true);
    }

    [Fact]
    public async Task A_rebuild_request_is_refused_while_a_rebuild_holds_the_lease()
    {
        var tenant = Guid.NewGuid();
        await SeedStateAsync(tenant, state => state.TryStartBuild(7, DateTime.UtcNow, TimeSpan.FromHours(1)));

        var conflict = await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new RebuildSearchIndexCommand(Key)))
            .Should().ThrowAsync<ConflictException>();

        conflict.Which.ErrorCode.Should().Be("search_rebuild_running");
        fixture.Jobs.For(tenant).Should().BeEmpty();
    }

    [Fact]
    public async Task A_drift_check_needs_a_built_index_and_then_enqueues_the_check_in_the_callers_tenant()
    {
        var tenant = Guid.NewGuid();

        var conflict = await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new CheckSearchIndexDriftCommand(Key)))
            .Should().ThrowAsync<ConflictException>();
        conflict.Which.ErrorCode.Should().Be("search_index_not_built");

        await fixture.MarkBuiltAsync(tenant);
        await fixture.SendAsync(tenant, new CheckSearchIndexDriftCommand(Key));

        var job = fixture.Jobs.For(tenant).Should().ContainSingle().Which.Job;
        job.Type.Should().Be<DetectSearchDriftJob>();
        job.Method.Name.Should().Be(nameof(DetectSearchDriftJob.CheckIndexAsync));
        job.Args[0].Should().Be(Key);
    }

    [Fact]
    public async Task The_requested_drift_check_records_its_findings_on_the_index()
    {
        var tenant = Guid.NewGuid();
        fixture.Articles.Put(tenant, Article("a1", "Laptop"));
        await fixture.MarkBuiltAsync(tenant);

        var drifted = await fixture.RunAsync(tenant, sp =>
            sp.GetRequiredService<DetectSearchDriftJob>().CheckIndexAsync(Key, JobCancellationToken.Null));

        drifted.Should().Be(1, "the article was never indexed");
        (await fixture.SendAsync(tenant, new GetSearchIndexByKeyQuery(Key))).Drift!.Missing.Should().Be(1);
    }

    [Fact]
    public async Task The_platform_sees_the_engine_and_every_definition()
    {
        using var host = TenantScope.BeginHostScope();
        await using var scope = fixture.Services.CreateAsyncScope();

        var engine = await scope.ServiceProvider.GetRequiredService<Shared.Contracts.Mediation.ISender>()
            .Send(new GetSearchEngineQuery());

        engine.Engine.Should().Be("Postgres");
        var index = engine.Indexes.Should().ContainSingle(i => i.Key == Key).Which;
        index.Fingerprint.Should().Be(fixture.Index.Fingerprint);
        index.FilterFields.Should().HaveCount(Definition.FilterFields.Count);
    }

    [Fact]
    public async Task A_rebuild_in_every_tenant_fans_out_one_forced_rebuild_per_tenant()
    {
        using (TenantScope.BeginHostScope())
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<Shared.Contracts.Mediation.ISender>();

            (await sender.Send(new RebuildSearchIndexInAllTenantsCommand(Key))).IndexKey.Should().Be(Key);
            await FluentActions.Awaiting(() => sender.Send(new RebuildSearchIndexInAllTenantsCommand("test.removed")))
                .Should().ThrowAsync<NotFoundException>();
        }

        var fanOut = fixture.Jobs.All.Should().ContainSingle(j =>
            j.Job.Method.Name == nameof(ReconcileSearchIndexesJob.RebuildAllTenantsAsync) && (string)j.Job.Args[0] == Key).Which;
        fanOut.TenantId.Should().BeNull("the fan-out runs without a tenant and visits every one");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ReconcileSearchIndexesJob>()
                .RebuildAllTenantsAsync(Key, JobCancellationToken.Null)).Should().Be(2);
            (await scope.ServiceProvider.GetRequiredService<ReconcileSearchIndexesJob>()
                .RebuildAllTenantsAsync("test.removed", JobCancellationToken.Null)).Should().Be(0);
        }

        foreach (var tenant in new[] { SearchPostgresFixture.TenantA.Id, SearchPostgresFixture.TenantB.Id })
        {
            fixture.Jobs.For(tenant).Should().Contain(j =>
                j.Job.Type == typeof(RebuildSearchIndexJob) && (string)j.Job.Args[0] == Key && (bool)j.Job.Args[1]);
        }
    }
}
