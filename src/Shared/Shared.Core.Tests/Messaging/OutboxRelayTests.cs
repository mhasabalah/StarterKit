using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using static Shared.Core.Tests.Messaging.OutboxFixture;

namespace Shared.Core.Tests.Messaging;

/// <summary>The read side: claim, publish, record the outcome. Real PostgreSQL (lease UPDATE, ExecuteUpdate).</summary>
[Collection(OutboxCollection.Name)]
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private readonly OutboxFixture _fx;

    public OutboxRelayTests(OutboxFixture fx) => _fx = fx;

    public Task InitializeAsync() => _fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ----- happy path ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pending_rows_are_published_once_with_the_row_id_as_message_id_and_marked_processed()
    {
        var rows = Enumerable.Range(0, 3).Select(i => _fx.NewHostRow(r => r.OccurredOn = TestClock.Start.AddSeconds(i))).ToArray();
        await _fx.SeedAsync(rows);

        var published = await _fx.DeliverHostAsync();

        published.Should().Be(3);
        _fx.Bus.Published.Select(p => p.MessageId).Should().Equal(rows.Select(r => (Guid?)r.Id), "oldest first");
        _fx.Bus.Published.Should().OnlyContain(p => p.Type == typeof(ThingHappenedIntegrationEvent) &&
                                                    p.Message is ThingHappenedIntegrationEvent);
        foreach (var row in rows)
        {
            var stored = await _fx.HostRow(row.Id);
            stored.ProcessedAt.Should().Be(TestClock.Start);
            stored.LockToken.Should().BeNull();
            stored.LockedUntil.Should().BeNull();
            stored.LastError.Should().BeNull();
            stored.DeadLetteredAt.Should().BeNull();
        }

        var first = (ThingHappenedIntegrationEvent)_fx.Bus.Published[0].Message;
        first.EventId.Should().Be(rows[0].Id);
        first.Note.Should().Be("seeded");

        (await _fx.DeliverHostAsync()).Should().Be(0, "delivered rows are never published again");
        _fx.Bus.Published.Should().HaveCount(3);
    }

    [Fact]
    public async Task Backlog_larger_than_a_batch_is_drained_in_rounds()
    {
        _fx.Options.BatchSize = 4;
        await _fx.SeedAsync(Enumerable.Range(0, 10).Select(_ => _fx.NewHostRow()).ToArray());

        (await _fx.DeliverHostAsync()).Should().Be(10);
        _fx.Bus.Published.Select(p => p.MessageId).Distinct().Should().HaveCount(10);
    }

    [Fact]
    public async Task Two_relays_running_concurrently_publish_every_row_exactly_once()
    {
        _fx.Options.BatchSize = 7;
        var rows = Enumerable.Range(0, 80).Select(i => _fx.NewHostRow(r => r.OccurredOn = TestClock.Start.AddMilliseconds(i))).ToArray();
        await _fx.SeedAsync(rows);

        async Task<int> RunRelay()
        {
            await Task.Yield();
            using (TenantScope.BeginHostScope())
            {
                return await _fx.InScope(sp => sp.GetRequiredService<OutboxRelay>()
                    .DeliverAsync(sp.GetRequiredService<OutboxHostDbContext>()));
            }
        }

        var counts = await Task.WhenAll(RunRelay(), RunRelay(), RunRelay());

        counts.Sum().Should().Be(80, "every row was claimed by exactly one relay");
        var ids = _fx.Bus.Published.Select(p => p.MessageId!.Value).ToList();
        ids.Should().HaveCount(80).And.OnlyHaveUniqueItems();
        ids.Should().BeEquivalentTo(rows.Select(r => r.Id));
        (await _fx.HostRows()).Should().OnlyContain(r => r.ProcessedAt != null && r.LockToken == null);
    }

    // ----- failures ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_failure_records_the_attempt_schedules_a_back_off_and_releases_the_lease()
    {
        var row = _fx.NewHostRow();
        await _fx.SeedAsync(row);
        _fx.Bus.Failure = _ => new InvalidOperationException("bus down");

        (await _fx.DeliverHostAsync()).Should().Be(0);

        var stored = await _fx.HostRow(row.Id);
        stored.Attempts.Should().Be(1);
        stored.LastError.Should().Contain("InvalidOperationException").And.Contain("bus down");
        stored.NextAttemptAt.Should().Be(TestClock.Start.AddSeconds(5));
        stored.LockToken.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
        stored.ProcessedAt.Should().BeNull();
        stored.DeadLetteredAt.Should().BeNull();
    }

    [Fact]
    public async Task Row_that_is_not_due_yet_is_skipped_until_its_back_off_elapsed()
    {
        var row = _fx.NewHostRow();
        await _fx.SeedAsync(row);
        _fx.Bus.Failure = _ => new InvalidOperationException("bus down");
        await _fx.DeliverHostAsync();
        _fx.Bus.Failure = null;

        _fx.Clock.Advance(TimeSpan.FromSeconds(4));
        (await _fx.DeliverHostAsync()).Should().Be(0, "still inside the 5s back-off");
        _fx.Bus.Published.Should().BeEmpty();

        _fx.Clock.Advance(TimeSpan.FromSeconds(1));
        (await _fx.DeliverHostAsync()).Should().Be(1);
        var stored = await _fx.HostRow(row.Id);
        stored.ProcessedAt.Should().NotBeNull();
        stored.Attempts.Should().Be(1, "a success does not touch the failure history");
        stored.LastError.Should().BeNull("a delivered row carries no error");
    }

    [Fact]
    public async Task Back_off_doubles_with_every_failed_attempt()
    {
        var row = _fx.NewHostRow();
        await _fx.SeedAsync(row);
        _fx.Bus.Failure = _ => new InvalidOperationException("still down");

        await _fx.DeliverHostAsync();
        _fx.Clock.Advance(TimeSpan.FromSeconds(5));
        await _fx.DeliverHostAsync();

        var stored = await _fx.HostRow(row.Id);
        stored.Attempts.Should().Be(2);
        stored.NextAttemptAt.Should().Be(_fx.Clock.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task Row_is_dead_lettered_after_max_attempts_and_never_retried()
    {
        _fx.Options.MaxAttempts = 2;
        var row = _fx.NewHostRow();
        await _fx.SeedAsync(row);
        _fx.Bus.Failure = _ => new InvalidOperationException("down");

        await _fx.DeliverHostAsync();
        (await _fx.HostRow(row.Id)).DeadLetteredAt.Should().BeNull("one attempt left");
        _fx.Clock.Advance(TimeSpan.FromSeconds(5));
        await _fx.DeliverHostAsync();

        var stored = await _fx.HostRow(row.Id);
        stored.Attempts.Should().Be(2);
        stored.DeadLetteredAt.Should().Be(_fx.Clock.UtcNow);
        stored.ProcessedAt.Should().BeNull();

        _fx.Bus.Failure = null;
        _fx.Clock.Advance(TimeSpan.FromDays(30));
        (await _fx.DeliverHostAsync()).Should().Be(0);
        _fx.Bus.Published.Should().BeEmpty("a dead letter needs an operator, not a retry");
    }

    [Fact]
    public async Task Unknown_urn_is_dead_lettered_immediately_without_publishing()
    {
        var row = _fx.NewHostRow(r => r.MessageType = "urn:message:shared-tests.renamed-or-removed.v1");
        await _fx.SeedAsync(row);

        (await _fx.DeliverHostAsync()).Should().Be(0);

        var stored = await _fx.HostRow(row.Id);
        stored.DeadLetteredAt.Should().NotBeNull();
        stored.Attempts.Should().Be(1);
        stored.LastError.Should().Contain("Unknown integration event type").And.Contain("renamed-or-removed");
        _fx.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreadable_payload_is_dead_lettered_immediately()
    {
        var row = _fx.NewHostRow(r => r.Payload = """{"eventId":"not-a-guid"}""");
        await _fx.SeedAsync(row);

        await _fx.DeliverHostAsync();

        var stored = await _fx.HostRow(row.Id);
        stored.DeadLetteredAt.Should().NotBeNull();
        stored.LastError.Should().Contain("cannot be read");
        _fx.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Row_of_a_tenant_that_no_longer_exists_is_dead_lettered_immediately()
    {
        var row = _fx.NewHostRow(tenantId: GoneTenantId);
        await _fx.SeedAsync(row);

        await _fx.DeliverHostAsync();

        var stored = await _fx.HostRow(row.Id);
        stored.DeadLetteredAt.Should().NotBeNull();
        stored.LastError.Should().Contain(GoneTenantId.ToString()).And.Contain("no longer exists");
        _fx.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task One_failing_row_does_not_block_the_others_of_its_batch()
    {
        var bad = _fx.NewHostRow(r => { r.OccurredOn = TestClock.Start; r.Payload = r.Payload.Replace("seeded", "poison"); });
        var good = _fx.NewHostRow(r => r.OccurredOn = TestClock.Start.AddSeconds(1));
        await _fx.SeedAsync(bad, good);
        _fx.Bus.Failure = m => m is ThingHappenedIntegrationEvent { Note: "poison" } ? new InvalidOperationException("poison") : null;

        (await _fx.DeliverHostAsync()).Should().Be(1);

        _fx.Bus.Published.Single().MessageId.Should().Be(good.Id);
        (await _fx.HostRow(bad.Id)).Attempts.Should().Be(1);
        (await _fx.HostRow(good.Id)).ProcessedAt.Should().NotBeNull();
    }

    // ----- targeting, leases ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Targeted_delivery_only_touches_the_requested_ids()
    {
        var rows = Enumerable.Range(0, 3).Select(i => _fx.NewHostRow(r => r.OccurredOn = TestClock.Start.AddSeconds(i))).ToArray();
        await _fx.SeedAsync(rows);

        (await _fx.DeliverHostAsync([rows[0].Id, rows[2].Id])).Should().Be(2);

        _fx.Bus.Published.Select(p => p.MessageId!.Value).Should().BeEquivalentTo([rows[0].Id, rows[2].Id]);
        (await _fx.HostRow(rows[1].Id)).ProcessedAt.Should().BeNull();
        (await _fx.DeliverHostAsync([])).Should().Be(0, "an empty id list means nothing to do, not everything");
        (await _fx.HostRow(rows[1].Id)).ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Row_leased_by_another_relay_is_skipped_until_the_lease_expires()
    {
        var row = _fx.NewHostRow(r =>
        {
            r.LockToken = Guid.NewGuid();
            r.LockedUntil = TestClock.Start.AddSeconds(30);
        });
        await _fx.SeedAsync(row);

        (await _fx.DeliverHostAsync()).Should().Be(0);
        _fx.Bus.Published.Should().BeEmpty();

        _fx.Clock.Advance(TimeSpan.FromSeconds(31));
        (await _fx.DeliverHostAsync()).Should().Be(1, "a crashed relay's lease expires");
        _fx.Bus.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task Context_without_an_outbox_cannot_be_delivered()
    {
        using (TenantScope.BeginHostScope())
        {
            await _fx.InScope(async sp =>
            {
                var act = () => sp.GetRequiredService<OutboxRelay>().DeliverAsync(sp.GetRequiredService<NoOutboxDbContext>());
                (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("maps no outbox");
            });
        }
    }

    // ----- retention -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Purge_deletes_only_delivered_rows_older_than_the_retention()
    {
        var now = _fx.Clock.UtcNow;
        var oldDelivered = _fx.NewHostRow(r => r.ProcessedAt = now.AddDays(-8));
        var recentDelivered = _fx.NewHostRow(r => r.ProcessedAt = now.AddDays(-6));
        var oldPending = _fx.NewHostRow(r => r.OccurredOn = now.AddDays(-30));
        var oldDeadLetter = _fx.NewHostRow(r =>
        {
            r.OccurredOn = now.AddDays(-30);
            r.DeadLetteredAt = now.AddDays(-20);
        });
        await _fx.SeedAsync(oldDelivered, recentDelivered, oldPending, oldDeadLetter);

        using (TenantScope.BeginHostScope())
        {
            var purged = await _fx.InScope(sp => sp.GetRequiredService<OutboxRelay>()
                .PurgeDeliveredAsync(sp.GetRequiredService<OutboxHostDbContext>()));
            purged.Should().Be(1);
        }

        (await _fx.HostRows()).Select(r => r.Id).Should()
            .BeEquivalentTo([recentDelivered.Id, oldPending.Id, oldDeadLetter.Id]);
    }

    // ----- tenants -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tenant_relay_never_sees_another_tenants_rows_and_publishes_inside_the_row_tenant_scope()
    {
        var a = _fx.NewTenantRow(TenantA.Id);
        var b = _fx.NewTenantRow(TenantB.Id);
        await _fx.SeedAsync(a, b);

        async Task<int> DeliverAs(TenantInfo tenant)
        {
            using (TenantScope.Begin(tenant))
            {
                return await _fx.InScope(sp => sp.GetRequiredService<OutboxRelay>()
                    .DeliverAsync(sp.GetRequiredService<OutboxTenantDbContext>()));
            }
        }

        (await DeliverAs(TenantA)).Should().Be(1);
        var publishedForA = _fx.Bus.Published.Should().ContainSingle().Subject;
        publishedForA.MessageId.Should().Be(a.Id);
        publishedForA.TenantId.Should().Be(TenantA.Id, "consumers/headers must see the row's tenant");
        publishedForA.IsHostScope.Should().BeFalse();
        (await _fx.TenantRow(b.Id)).ProcessedAt.Should().BeNull("tenant A's relay cannot even see tenant B's row");

        (await DeliverAs(TenantB)).Should().Be(1);
        _fx.Bus.Published.Last().TenantId.Should().Be(TenantB.Id);
        (await _fx.TenantRows()).Should().OnlyContain(r => r.ProcessedAt != null);
    }

    [Fact]
    public async Task Row_raised_in_host_scope_is_published_in_host_scope_of_its_tenant()
    {
        var row = _fx.NewTenantRow(TenantA.Id, r => r.HostScope = true);
        await _fx.SeedAsync(row);

        using (TenantScope.BeginHostScope())
        {
            (await _fx.InScope(sp => sp.GetRequiredService<OutboxRelay>()
                .DeliverAsync(sp.GetRequiredService<OutboxTenantDbContext>()))).Should().Be(1);
        }

        var published = _fx.Bus.Published.Should().ContainSingle().Subject;
        published.IsHostScope.Should().BeTrue();
        published.TenantId.Should().Be(TenantA.Id);
    }

    // ----- immediate delivery handler ------------------------------------------------------------------------------

    [Fact]
    public async Task Saved_notification_delivers_exactly_the_saved_rows_in_the_tenant_of_the_save()
    {
        var other = _fx.NewTenantRow(TenantA.Id);
        await _fx.SeedAsync(other);
        var thing = TenantThing.Create().RaiseThingHappened();
        var eventId = thing.IntegrationEvents.Single().EventId;
        OutboxMessagesSaved? notification = null;

        using (TenantScope.Begin(TenantA))
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxTenantDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
                notification = sp.GetRequiredService<IDomainEventCollector>().DequeueAll().OfType<OutboxMessagesSaved>().Single();
            });
        }

        // Dispatch happens after the unit of work: no ambient tenant any more.
        await _fx.InScope(sp => sp.GetRequiredService<Shared.Contracts.Mediation.INotificationHandler<OutboxMessagesSaved>>().Handle(notification!, CancellationToken.None));

        var published = _fx.Bus.Published.Should().ContainSingle().Subject;
        published.MessageId.Should().Be(eventId);
        published.TenantId.Should().Be(TenantA.Id);
        (await _fx.TenantRow(eventId)).ProcessedAt.Should().NotBeNull();
        (await _fx.TenantRow(other.Id)).ProcessedAt.Should().BeNull("only the rows of the save are delivered immediately; the sweep owns the rest");
    }

    [Fact]
    public async Task Sweep_job_delivers_host_rows_and_every_tenants_rows_and_purges_old_deliveries()
    {
        var host = _fx.NewHostRow();
        var a = _fx.NewTenantRow(TenantA.Id);
        var b = _fx.NewTenantRow(TenantB.Id);
        var oldDelivered = _fx.NewHostRow(r => r.ProcessedAt = TestClock.Start.AddDays(-9));
        await _fx.SeedAsync(host, oldDelivered);
        await _fx.SeedAsync(a, b);

        await _fx.InScope(sp => sp.GetRequiredService<OutboxSweepJob>().ExecuteAsync(CancellationToken.None));

        _fx.Bus.Published.Select(p => p.MessageId!.Value).Should().BeEquivalentTo([host.Id, a.Id, b.Id]);
        _fx.Bus.Published.Single(p => p.MessageId == a.Id).TenantId.Should().Be(TenantA.Id);
        _fx.Bus.Published.Single(p => p.MessageId == b.Id).TenantId.Should().Be(TenantB.Id);
        (await _fx.HostRows()).Select(r => r.Id).Should().BeEquivalentTo([host.Id], "the old delivered row was purged");
    }
}
