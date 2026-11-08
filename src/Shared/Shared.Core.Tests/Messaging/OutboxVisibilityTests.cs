using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Shared.Core.Messaging.Outbox;
using static Shared.Core.Tests.Messaging.OutboxFixture;

namespace Shared.Core.Tests.Messaging;

/// <summary>Backlog gauges (fed by the sweep, never by the metrics callback) and the operator dead-letter service.</summary>
[Collection(OutboxCollection.Name)]
public sealed class OutboxVisibilityTests : IAsyncLifetime
{
    private const string HostContext = nameof(OutboxHostDbContext);
    private const string TenantContext = nameof(OutboxTenantDbContext);

    private readonly OutboxFixture _fx;

    public OutboxVisibilityTests(OutboxFixture fx) => _fx = fx;

    public async ValueTask InitializeAsync() => await _fx.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void DeadLetter(OutboxMessageBase row)
    {
        row.DeadLetteredAt = TestClock.Start;
        row.Attempts = 10;
        row.LastError = "boom";
    }

    private Task<T> Admin<T>(Func<OutboxAdminService, Task<T>> call) =>
        _fx.InScope(sp => call(sp.GetRequiredService<OutboxAdminService>()));

    // ----- metrics -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sweep_publishes_pending_and_dead_lettered_gauges_per_context_summed_over_tenants()
    {
        _fx.Bus.Failure = _ => new InvalidOperationException("bus down"); // rows stay pending
        await _fx.SeedAsync(_fx.NewHostRow(), _fx.NewHostRow(), _fx.NewHostRow(DeadLetter));
        await _fx.SeedAsync(_fx.NewTenantRow(TenantA.Id), _fx.NewTenantRow(TenantB.Id), _fx.NewTenantRow(TenantB.Id, DeadLetter));
        // A delivered row counts for neither gauge.
        await _fx.SeedAsync(_fx.NewHostRow(r => r.ProcessedAt = TestClock.Start));

        await _fx.InScope(sp => sp.GetRequiredService<OutboxSweepJob>().ExecuteAsync(CancellationToken.None));

        var gauges = ReadGauges();
        gauges[(OutboxMetrics.PendingInstrument, HostContext)].Should().Be(2);
        gauges[(OutboxMetrics.DeadLetteredInstrument, HostContext)].Should().Be(1);
        gauges[(OutboxMetrics.PendingInstrument, TenantContext)].Should().Be(2);
        gauges[(OutboxMetrics.DeadLetteredInstrument, TenantContext)].Should().Be(1);
    }

    [Fact]
    public void Gauges_report_cached_values_and_drop_a_snapshot_that_is_no_longer_refreshed()
    {
        var metrics = _fx.Services.GetRequiredService<OutboxMetrics>();
        metrics.Record("StaleProbeDbContext", 5, 2);

        ReadGauges()[(OutboxMetrics.PendingInstrument, "StaleProbeDbContext")].Should().Be(5);

        _fx.Clock.Advance(OutboxMetrics.StaleAfter + TimeSpan.FromSeconds(1));
        ReadGauges().Keys.Should().NotContain(k => k.Context == "StaleProbeDbContext");
    }

    private static Dictionary<(string Instrument, string Context), long> ReadGauges()
    {
        var values = new Dictionary<(string, string), long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name is OutboxMetrics.PendingInstrument or OutboxMetrics.DeadLetteredInstrument)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var context = tags.ToArray().Single(t => t.Key == "context").Value!.ToString()!;
            values[(instrument.Name, context)] = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    // ----- list ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_only_dead_letters_of_the_requested_context_paged()
    {
        var dead = Enumerable.Range(0, 3).Select(_ => _fx.NewHostRow(DeadLetter)).ToArray();
        await _fx.SeedAsync([.. dead, _fx.NewHostRow(), _fx.NewHostRow(r => r.ProcessedAt = TestClock.Start)]);
        await _fx.SeedAsync(_fx.NewTenantRow(TenantA.Id, DeadLetter));

        var page = await Admin(a => a.ListDeadLettersAsync(HostContext, null, 1, 2, CancellationToken.None));

        page.PageInfoResult.TotalItems.Should().Be(3);
        page.Result.Should().HaveCount(2);
        page.Result!.Should().OnlyContain(d => d.LastError == "boom" && d.Attempts == 10 && d.TenantId == null);
        (await Admin(a => a.ListDeadLettersAsync(HostContext.ToLowerInvariant(), null, 2, 2, CancellationToken.None)))
            .Result.Should().HaveCount(1, "context names are case-insensitive");
    }

    [Fact]
    public async Task List_of_a_tenant_context_spans_tenants_unless_a_tenant_is_given()
    {
        var a = _fx.NewTenantRow(TenantA.Id, DeadLetter);
        var b = _fx.NewTenantRow(TenantB.Id, DeadLetter);
        await _fx.SeedAsync(a, b, _fx.NewTenantRow(TenantA.Id));

        var all = await Admin(s => s.ListDeadLettersAsync(TenantContext, null, null, null, CancellationToken.None));
        all.Result!.Select(d => d.Id).Should().BeEquivalentTo([a.Id, b.Id]);
        all.Result!.Select(d => d.TenantId).Should().BeEquivalentTo([TenantA.Id, TenantB.Id]);

        var onlyB = await Admin(s => s.ListDeadLettersAsync(TenantContext, TenantB.Id, null, null, CancellationToken.None));
        onlyB.Result!.Select(d => d.Id).Should().Equal(b.Id);
    }

    [Fact]
    public async Task List_of_an_unknown_context_or_tenant_is_not_found()
    {
        await FluentActions.Awaiting(() => Admin(a => a.ListDeadLettersAsync("NopeDbContext", null, null, null, CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Admin(a => a.ListDeadLettersAsync(TenantContext, Guid.NewGuid(), null, null, CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
    }

    // ----- requeue -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Requeue_makes_a_dead_letter_deliverable_by_the_next_sweep()
    {
        var row = _fx.NewHostRow(DeadLetter);
        await _fx.SeedAsync(row);
        await _fx.InScope(sp => sp.GetRequiredService<OutboxSweepJob>().ExecuteAsync(CancellationToken.None));
        _fx.Bus.Published.Should().BeEmpty("dead letters are never redelivered by themselves");

        await Admin(async a =>
        {
            await a.RequeueAsync(HostContext, row.Id, null, CancellationToken.None);
            return true;
        });

        var stored = await _fx.HostRow(row.Id);
        stored.DeadLetteredAt.Should().BeNull();
        stored.Attempts.Should().Be(0);
        stored.NextAttemptAt.Should().Be(_fx.Clock.UtcNow);
        await _fx.InScope(sp => sp.GetRequiredService<OutboxSweepJob>().ExecuteAsync(CancellationToken.None));
        _fx.Bus.Published.Select(p => p.MessageId).Should().Equal(row.Id);
        (await _fx.HostRow(row.Id)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Requeue_of_a_row_that_is_not_dead_lettered_conflicts_and_changes_nothing()
    {
        var pending = _fx.NewHostRow(r => r.Attempts = 3);
        await _fx.SeedAsync(pending);

        await FluentActions.Awaiting(() => Admin(async a =>
            {
                await a.RequeueAsync(HostContext, pending.Id, null, CancellationToken.None);
                return true;
            }))
            .Should().ThrowAsync<ConflictException>();

        (await _fx.HostRow(pending.Id)).Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Requeue_of_a_missing_row_or_unknown_context_is_not_found()
    {
        await FluentActions.Awaiting(() => Admin(async a =>
            {
                await a.RequeueAsync(HostContext, Guid.NewGuid(), null, CancellationToken.None);
                return true;
            }))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Admin(a => a.RequeueAllAsync("NopeDbContext", null, CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Requeue_all_resets_every_dead_letter_of_the_context_and_leaves_other_rows_alone()
    {
        var a = _fx.NewTenantRow(TenantA.Id, DeadLetter);
        var b = _fx.NewTenantRow(TenantB.Id, DeadLetter);
        var delivered = _fx.NewTenantRow(TenantA.Id, r => r.ProcessedAt = TestClock.Start);
        await _fx.SeedAsync(a, b, delivered);

        // Targeted at tenant A: tenant B keeps its dead letter.
        (await Admin(s => s.RequeueAllAsync(TenantContext, TenantA.Id, CancellationToken.None))).Should().Be(1);
        (await _fx.TenantRow(a.Id)).DeadLetteredAt.Should().BeNull();
        (await _fx.TenantRow(b.Id)).DeadLetteredAt.Should().NotBeNull();

        // Without a tenant: the rest of the shared database.
        (await Admin(s => s.RequeueAllAsync(TenantContext, null, CancellationToken.None))).Should().Be(1);
        (await _fx.TenantRow(b.Id)).DeadLetteredAt.Should().BeNull();
        (await _fx.TenantRow(delivered.Id)).ProcessedAt.Should().Be(TestClock.Start);

        (await Admin(s => s.RequeueAllAsync(TenantContext, null, CancellationToken.None))).Should().Be(0);
    }
}
