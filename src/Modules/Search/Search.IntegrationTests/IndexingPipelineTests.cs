using Hangfire;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Search.Contracts.Indexing;
using Search.Contracts.Indexing.IntegrationEvents;
using Search.Contracts.Querying;
using Search.Data;
using Search.Domains.Indexes.Consumers;
using Search.Domains.Indexes.EventHandlers;
using Search.Domains.Indexes.Features.ApplySearchDocumentChanges;
using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Models;
using Search.Domains.Indexes.Services;
using Search.Engine;
using Search.Observability;
using Search.Options;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests;

/// <summary>
/// The indexing pipeline on the real module and the PostgreSQL engine: change events, rebuilds, reconcile, drift and
/// tenant purge. Each test uses fresh tenants and writes the "owner's" articles into <see cref="TestIndex.ArticleStore"/>.
/// </summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class IndexingPipelineTests(SearchPostgresFixture fixture)
{
    private Guid NewTenantWith(params SearchDocument[] articles)
    {
        var tenant = Guid.NewGuid();
        foreach (var article in articles)
            fixture.Articles.Put(tenant, article);
        return tenant;
    }

    private Task<IndexChangeResult> ApplyAsync(Guid tenant, params string[] ids) =>
        fixture.SendAsync(tenant, new ApplySearchDocumentChangesCommand(Key, ids));

    private Task<RebuildResult> RebuildAsync(Guid tenant, bool force = false, string key = Key) =>
        fixture.RunAsync(tenant, sp => sp.GetRequiredService<RebuildSearchIndexJob>().ExecuteAsync(key, force, null));

    private Task<SearchIndexState?> StateAsync(Guid tenant) =>
        fixture.RunAsync(tenant, sp => sp.GetRequiredService<SearchDbContext>().IndexStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IndexKey == Key));

    private async Task<string[]> IndexedIdsAsync(Guid tenant) =>
        (await fixture.RunAsync(tenant, async (ISearchEngine engine) =>
        {
            var ids = new List<string>();
            await foreach (var stamp in engine.StreamStampsAsync(new IndexScope(fixture.Index, tenant), CancellationToken.None))
                ids.Add(stamp.Id);
            return ids;
        })).Order().ToArray();

    private Task<long> RebuildStampOfAsync(Guid tenant, string id) =>
        fixture.AdminScalarAsync<long>(
            $"SELECT rebuild_stamp FROM search.search_documents WHERE tenant_id = '{tenant}' AND document_id = '{id}'")!;

    // ----- live changes ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_change_indexes_the_current_state_and_removes_what_the_source_no_longer_has()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"), Article("a2", "Lamp"));

        (await ApplyAsync(tenant, "a1", "a2", "gone")).Should().Be(new IndexChangeResult(2, 1));
        (await IndexedIdsAsync(tenant)).Should().Equal("a1", "a2");

        fixture.Articles.Remove(tenant, "a1");
        fixture.Articles.Put(tenant, Article("a2", "Desk lamp", version: 2));
        (await ApplyAsync(tenant, "a1", "a2")).Should().Be(new IndexChangeResult(1, 1));

        (await IndexedIdsAsync(tenant)).Should().Equal("a2");
    }

    [Fact]
    public async Task The_consumer_applies_the_event_in_its_tenant_and_repeats_are_harmless()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        var consumer = new SearchDocumentsChangedConsumer(
            fixture.Services.GetRequiredService<IUnitOfWorkRunner>(),
            fixture.Services.GetRequiredService<SearchIndexRegistry>(),
            fixture.Services.GetRequiredService<SearchMetrics>(),
            TimeProvider.System,
            NullLogger<SearchDocumentsChangedConsumer>.Instance);
        var context = new Mock<ConsumeContext<SearchDocumentsChangedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(SearchDocumentsChangedIntegrationEvent.For(Key, "a1"));
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        using (TenantScope.Begin(SearchPostgresFixture.Tenant(tenant))) // the bus restores the event's tenant
        {
            await consumer.Consume(context.Object);
            await consumer.Consume(context.Object);
        }

        (await IndexedIdsAsync(tenant)).Should().Equal("a1");
    }

    [Fact]
    public async Task The_consumer_skips_an_unknown_index()
    {
        var consumer = new SearchDocumentsChangedConsumer(
            fixture.Services.GetRequiredService<IUnitOfWorkRunner>(),
            fixture.Services.GetRequiredService<SearchIndexRegistry>(),
            fixture.Services.GetRequiredService<SearchMetrics>(),
            TimeProvider.System,
            NullLogger<SearchDocumentsChangedConsumer>.Instance);
        var context = new Mock<ConsumeContext<SearchDocumentsChangedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(SearchDocumentsChangedIntegrationEvent.For("test.removed", "x"));

        await FluentActions.Invoking(() => consumer.Consume(context.Object)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_change_with_a_document_that_does_not_fit_the_index_fails_loudly()
    {
        var tenant = NewTenantWith(SearchDocument.For("bad", 1).Text("title", "x").Keyword("colour", "red").Build());

        await FluentActions.Invoking(() => ApplyAsync(tenant, "bad")).Should().ThrowAsync<InvalidSearchDocumentException>();
    }

    [Fact]
    public async Task A_change_to_an_unknown_index_is_rejected_by_validation() =>
        await FluentActions.Invoking(() => fixture.SendAsync(Guid.NewGuid(), new ApplySearchDocumentChangesCommand("test.removed", ["x"])))
            .Should().ThrowAsync<FluentValidation.ValidationException>();

    // ----- rebuild ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_rebuild_indexes_every_document_in_batches_sweeps_orphans_and_makes_the_index_answer()
    {
        var tenant = NewTenantWith(Enumerable.Range(1, 5).Select(i => Article($"a{i}", $"Laptop {i}")).ToArray());
        fixture.Articles.Put(tenant, Article("zombie", "Laptop zombie"));
        await ApplyAsync(tenant, "zombie");           // indexed before the rebuild...
        fixture.Articles.Remove(tenant, "zombie");    // ...then removed without an event

        var result = await RebuildAsync(tenant);

        result.Should().Be(new RebuildResult(RebuildOutcome.Built, Documents: 5, Swept: 1));
        (await IndexedIdsAsync(tenant)).Should().Equal("a1", "a2", "a3", "a4", "a5");
        var state = (await StateAsync(tenant))!;
        state.Status.Should().Be(SearchIndexStatus.Ready);
        state.CanAnswer.Should().BeTrue();
        state.DocumentCount.Should().Be(5);
        state.DefinitionFingerprint.Should().Be(fixture.Index.Fingerprint);
        state.LeaseUntil.Should().BeNull();

        var answer = await fixture.SendAsync(tenant, new SearchDocumentsQuery { IndexKey = Key, Text = "laptop" });
        answer.Status.Should().Be(SearchStatus.Ok);
        answer.Total.Should().Be(5);
    }

    [Fact]
    public async Task A_built_index_is_not_rebuilt_unless_forced()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        (await RebuildAsync(tenant)).Outcome.Should().Be(RebuildOutcome.Built);

        (await RebuildAsync(tenant)).Outcome.Should().Be(RebuildOutcome.UpToDate);
        (await RebuildAsync(tenant, force: true)).Outcome.Should().Be(RebuildOutcome.Built);
    }

    [Fact]
    public async Task A_rebuild_waits_for_the_lease_another_worker_holds()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        await fixture.RunAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            var state = SearchIndexState.Create(Key, DateTime.UtcNow);
            state.TryStartBuild(99, DateTime.UtcNow, TimeSpan.FromHours(1));
            db.IndexStates.Add(state);
            return await db.SaveChangesAsync();
        });

        (await RebuildAsync(tenant, force: true)).Outcome.Should().Be(RebuildOutcome.Busy);
    }

    [Fact]
    public async Task Changes_during_a_rebuild_carry_its_stamp_so_the_sweep_keeps_them()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        await fixture.RunAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            var state = SearchIndexState.Create(Key, DateTime.UtcNow);
            state.TryStartBuild(4242, DateTime.UtcNow, TimeSpan.FromHours(1));
            db.IndexStates.Add(state);
            return await db.SaveChangesAsync();
        });

        await ApplyAsync(tenant, "a1");

        (await RebuildStampOfAsync(tenant, "a1")).Should().Be(4242);
    }

    [Fact]
    public async Task A_failed_first_rebuild_fails_the_index_and_a_failed_rebuild_keeps_the_previous_build()
    {
        var bad = SearchDocument.For("bad", 1).Text("title", "x").Keyword("colour", "red").Build();
        var first = NewTenantWith(bad);

        (await RebuildAsync(first)).Outcome.Should().Be(RebuildOutcome.Failed);
        var failed = (await StateAsync(first))!;
        failed.Status.Should().Be(SearchIndexStatus.Failed);
        failed.LastErrorCode.Should().Be("invalid_document");
        failed.CanAnswer.Should().BeFalse();

        var later = NewTenantWith(Article("a1", "Laptop"));
        (await RebuildAsync(later)).Outcome.Should().Be(RebuildOutcome.Built);
        fixture.Articles.Put(later, bad);
        (await RebuildAsync(later, force: true)).Outcome.Should().Be(RebuildOutcome.Failed);

        var kept = (await StateAsync(later))!;
        kept.Status.Should().Be(SearchIndexStatus.Ready);
        kept.CanAnswer.Should().BeTrue();
        (await fixture.SendAsync(later, new SearchDocumentsQuery { IndexKey = Key, Text = "laptop" })).Total.Should().Be(1);
    }

    [Fact]
    public async Task A_rebuild_of_an_unknown_index_does_nothing() =>
        (await RebuildAsync(Guid.NewGuid(), key: "test.removed")).Outcome.Should().Be(RebuildOutcome.UnknownIndex);

    // ----- reconcile -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_enqueues_a_rebuild_in_the_tenant_of_each_index_that_needs_one()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        var job = (ReconcileSearchIndexesJob job) => job.ReconcileCurrentTenantAsync(null);

        (await fixture.RunAsync(tenant, sp => job(sp.GetRequiredService<ReconcileSearchIndexesJob>()))).Should().Be(1);
        var enqueued = fixture.Jobs.For(tenant).Should().ContainSingle().Which;
        enqueued.Job.Type.Should().Be<RebuildSearchIndexJob>();
        enqueued.Job.Args[0].Should().Be(Key);
        enqueued.Job.Args[1].Should().Be(false);

        await RebuildAsync(tenant);
        (await fixture.RunAsync(tenant, sp => job(sp.GetRequiredService<ReconcileSearchIndexesJob>()))).Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_runs_for_every_tenant()
    {
        await fixture.RunAsync(Guid.NewGuid(), sp => sp.GetRequiredService<ReconcileSearchIndexesJob>().ExecuteAsync(null));

        fixture.Jobs.For(SearchPostgresFixture.TenantA.Id).Should().NotBeEmpty();
        fixture.Jobs.For(SearchPostgresFixture.TenantB.Id).Should().NotBeEmpty();
    }

    // ----- drift -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Drift_finds_missing_stale_and_orphaned_documents_and_repairs_them()
    {
        var tenant = NewTenantWith(Article("same", "Laptop"), Article("stale", "Lamp"), Article("orphan", "Chair"));
        await RebuildAsync(tenant);

        // Changes that never raised an event:
        fixture.Articles.Put(tenant, Article("missing", "Desk"));
        fixture.Articles.Put(tenant, Article("stale", "Desk lamp", version: 2));
        fixture.Articles.Remove(tenant, "orphan");

        var report = (await CheckDriftAsync(tenant)).Single();

        report.Should().Be(new DriftReport(Key, Missing: 1, Stale: 1, Orphaned: 1, Repaired: 3));
        var state = (await StateAsync(tenant))!;
        (state.DriftMissing, state.DriftStale, state.DriftOrphaned).Should().Be((1, 1, 1));
        state.DriftCheckedAt.Should().NotBeNull();
        (await IndexedIdsAsync(tenant)).Should().Equal("missing", "same", "stale");
        (await CheckDriftAsync(tenant)).Single().Should().Be(new DriftReport(Key, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Drift_only_reports_when_auto_repair_is_off()
    {
        var tenant = NewTenantWith(Article("a1", "Laptop"));
        await RebuildAsync(tenant);
        fixture.Articles.Put(tenant, Article("a2", "Desk"));
        await using var reportOnly = fixture.BuildProvider(s => s.PostConfigure<SearchOptions>(o => o.Drift.AutoRepair = false));

        var report = await fixture.RunAsync(tenant,
            sp => sp.GetRequiredService<DetectSearchDriftJob>().CheckCurrentTenantAsync(null, null), services: reportOnly);

        report.Single().Should().Be(new DriftReport(Key, Missing: 1, Stale: 0, Orphaned: 0, Repaired: 0));
        (await IndexedIdsAsync(tenant)).Should().Equal("a1");
    }

    [Fact]
    public async Task Drift_skips_indexes_that_were_never_built() =>
        (await CheckDriftAsync(NewTenantWith(Article("a1", "Laptop")))).Should().BeEmpty();

    private Task<IReadOnlyList<DriftReport>> CheckDriftAsync(Guid tenant) =>
        fixture.RunAsync(tenant, sp => sp.GetRequiredService<DetectSearchDriftJob>().CheckCurrentTenantAsync(null, null));

    // ----- tenant purge ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tenant_purge_removes_its_documents_only()
    {
        var gone = NewTenantWith(Article("a1", "Laptop"));
        var kept = NewTenantWith(Article("b1", "Laptop"));
        await ApplyAsync(gone, "a1");
        await ApplyAsync(kept, "b1");

        await fixture.RunAsync(gone, async sp =>
        {
            await new SearchTenantPurgeHandler(sp.GetRequiredService<ISearchEngine>(), NullLogger<SearchTenantPurgeHandler>.Instance)
                .Handle(new TenantDataPurgeRequestedEvent(gone, "gone", TenantIsolation.Shared), CancellationToken.None);
            return true;
        });

        (await IndexedIdsAsync(gone)).Should().BeEmpty();
        (await IndexedIdsAsync(kept)).Should().Equal("b1");
    }
}
