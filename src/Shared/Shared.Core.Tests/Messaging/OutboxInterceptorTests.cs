using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using static Shared.Core.Tests.Messaging.OutboxFixture;

namespace Shared.Core.Tests.Messaging;

/// <summary>The write side: events become outbox rows in the SAME SaveChanges as the aggregate.</summary>
[Collection(OutboxCollection.Name)]
public sealed class OutboxInterceptorTests : IAsyncLifetime
{
    private const string ThingUrn = "urn:message:shared-tests.thing-happened.v1";

    private readonly OutboxFixture _fx;

    public OutboxInterceptorTests(OutboxFixture fx) => _fx = fx;

    public async ValueTask InitializeAsync() => await _fx.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Saving_an_aggregate_writes_one_outbox_row_with_urn_payload_and_metadata()
    {
        var thing = HostThing.Create().RaiseThingHappened("payload-note");
        var raised = (ThingHappenedIntegrationEvent)thing.IntegrationEvents.Single();

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
            });
        }

        var row = await _fx.HostRow(raised.EventId);
        row.MessageType.Should().Be(ThingUrn);
        row.OccurredOn.Should().BeCloseTo(raised.OccurredOn, TimeSpan.FromMilliseconds(1));
        row.TenantId.Should().BeNull("no tenant was ambient when the event was raised");
        row.HostScope.Should().BeFalse();
        row.Attempts.Should().Be(0);
        row.NextAttemptAt.Should().Be(TestClock.Start);
        row.ProcessedAt.Should().BeNull();
        row.DeadLetteredAt.Should().BeNull();
        row.LockToken.Should().BeNull();

        var payload = JsonSerializer.Deserialize<ThingHappenedIntegrationEvent>(row.Payload, OutboxFixture.Json)!;
        payload.EventId.Should().Be(raised.EventId);
        payload.ThingId.Should().Be(thing.Id);
        payload.Note.Should().Be("payload-note");
        payload.OccurredOn.Should().BeCloseTo(raised.OccurredOn, TimeSpan.FromMilliseconds(1));

        thing.IntegrationEvents.Should().BeEmpty("the events were taken over by the outbox");
    }

    [Fact]
    public async Task Host_row_carries_the_ambient_tenant()
    {
        var thing = HostThing.Create().RaiseThingHappened();
        var eventId = thing.IntegrationEvents.Single().EventId;

        using (TenantScope.Begin(TenantA))
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
            });
        }

        (await _fx.HostRow(eventId)).TenantId.Should().Be(TenantA.Id);
    }

    [Fact]
    public async Task Every_event_of_every_aggregate_gets_its_own_row()
    {
        var first = HostThing.Create().RaiseThingHappened("one").RaiseThingHappened("two");
        var second = HostThing.Create().RaiseThingHappened("three");
        var expected = first.IntegrationEvents.Concat(second.IntegrationEvents).Select(e => e.EventId).ToList();

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.AddRange(first, second);
                await db.SaveChangesAsync();
            });
        }

        (await _fx.HostRows()).Select(r => r.Id).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task Tenant_outbox_row_copies_the_tenant_of_the_aggregate()
    {
        var thing = TenantThing.Create().RaiseThingHappened();
        var eventId = thing.IntegrationEvents.Single().EventId;

        using (TenantScope.Begin(TenantA))
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxTenantDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
            });
        }

        var row = await _fx.TenantRow(eventId);
        row.TenantId.Should().Be(TenantA.Id).And.Be(thing.TenantId);
        row.HostScope.Should().BeFalse();
        row.MessageType.Should().Be(ThingUrn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_host_scope_the_tenant_of_the_aggregate_wins_over_the_ambient_one(bool ambientTenant)
    {
        // Platform work (impersonation-free admin job) creating data FOR tenant B.
        var thing = TenantThing.Create().RaiseThingHappened();
        thing.TenantId = TenantB.Id;
        var eventId = thing.IntegrationEvents.Single().EventId;

        using (ambientTenant ? TenantScope.BeginHostScope(TenantA) : TenantScope.BeginHostScope())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxTenantDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
            });
        }

        var row = await _fx.TenantRow(eventId);
        row.TenantId.Should().Be(TenantB.Id);
        row.HostScope.Should().BeTrue("the relay re-enters host scope when publishing");
    }

    [Fact]
    public async Task Rolled_back_transaction_leaves_no_row_and_no_ready_notification()
    {
        var thing = HostThing.Create().RaiseThingHappened();
        var eventId = thing.IntegrationEvents.Single().EventId;
        int readyBeforeRollback = -1, readyAfterRollback = -1;

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                var collector = sp.GetRequiredService<IDomainEventCollector>();

                await using var tx = await db.Database.BeginTransactionAsync();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
                readyBeforeRollback = collector.ReadyCount;
                await tx.RollbackAsync();
                readyAfterRollback = collector.ReadyCount;
            });
        }

        readyBeforeRollback.Should().Be(0, "the rows are not committed yet");
        readyAfterRollback.Should().Be(0, "a rolled-back save must never trigger immediate delivery");
        (await _fx.HostRows()).Should().BeEmpty();
    }

    [Fact]
    public async Task Committed_transaction_makes_the_notification_ready_with_the_saved_ids_and_scope()
    {
        var thing = TenantThing.Create().RaiseThingHappened();
        var eventId = thing.IntegrationEvents.Single().EventId;
        var readyBeforeCommit = -1;
        OutboxMessagesSaved? notification = null;

        using (TenantScope.Begin(TenantA))
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxTenantDbContext>();
                var collector = sp.GetRequiredService<IDomainEventCollector>();

                await using var tx = await db.Database.BeginTransactionAsync();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
                readyBeforeCommit = collector.ReadyCount;
                await tx.CommitAsync();

                collector.ReadyCount.Should().Be(1);
                notification = collector.DequeueAll().Should().ContainSingle().Subject.Should()
                    .BeOfType<OutboxMessagesSaved>().Subject;
            });
        }

        readyBeforeCommit.Should().Be(0, "events wait for the transaction outcome");
        notification!.ContextType.Should().Be<OutboxTenantDbContext>();
        notification.MessageIds.Should().Equal(eventId);
        notification.Tenant.Should().Be(TenantA);
        notification.IsHostScope.Should().BeFalse();
        (await _fx.TenantRow(eventId)).Should().NotBeNull();
    }

    [Fact]
    public async Task Save_without_a_transaction_stages_a_ready_notification_in_host_scope_form()
    {
        var thing = HostThing.Create().RaiseThingHappened();
        OutboxMessagesSaved? notification = null;

        using (TenantScope.BeginHostScope())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.Add(thing);
                await db.SaveChangesAsync();
                notification = sp.GetRequiredService<IDomainEventCollector>().DequeueAll()
                    .OfType<OutboxMessagesSaved>().Single();
            });
        }

        notification!.IsHostScope.Should().BeTrue();
        notification.ContextType.Should().Be<OutboxHostDbContext>();
    }

    [Fact]
    public async Task Failed_save_discards_the_notification_and_writes_no_row()
    {
        var name = $"dup-{Guid.NewGuid():N}";
        var collector = (IDomainEventCollector?)null;
        var readyAfterFailure = -1;

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                collector = sp.GetRequiredService<IDomainEventCollector>();
                db.Things.Add(HostThing.Create(name).RaiseThingHappened());
                await db.SaveChangesAsync(); // the first one commits (and stays)
                collector.DequeueAll();

                var duplicate = HostThing.Create(name).RaiseThingHappened();
                db.Things.Add(duplicate);
                var save = () => db.SaveChangesAsync();
                await save.Should().ThrowAsync<DbUpdateException>();
                readyAfterFailure = collector.ReadyCount;
            });
        }

        readyAfterFailure.Should().Be(0, "a failed save must not trigger delivery");
        (await _fx.HostRows()).Should().ContainSingle("only the committed first save wrote a row");
    }

    [Fact]
    public async Task Context_without_an_outbox_throws_naming_the_fix_and_the_aggregate_keeps_its_events()
    {
        var thing = HostThing.Create().RaiseThingHappened();
        Func<Task>? save = null;

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<NoOutboxDbContext>();
                db.Things.Add(thing);
                save = () => db.SaveChangesAsync();
                var ex = (await save.Should().ThrowAsync<InvalidOperationException>()).Which;
                ex.Message.Should().Contain(nameof(NoOutboxDbContext))
                    .And.Contain(nameof(ThingHappenedIntegrationEvent))
                    .And.Contain("AddOutbox()")
                    .And.Contain("AddTenantOutbox()");
            });
        }

        thing.IntegrationEvents.Should().ContainSingle("nothing was lost");
    }

    [Fact]
    public async Task Event_without_a_message_urn_is_refused_and_not_lost()
    {
        var thing = HostThing.Create().Raise(new UnpinnedIntegrationEvent());

        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.Add(thing);
                var save = () => db.SaveChangesAsync();
                (await save.Should().ThrowAsync<InvalidOperationException>()).Which.Message
                    .Should().Contain(nameof(UnpinnedIntegrationEvent)).And.Contain("MessageUrn");
            });
        }

        thing.IntegrationEvents.Should().ContainSingle("a refused event stays on the aggregate");
        (await _fx.HostRows()).Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregates_without_integration_events_write_no_rows_and_no_notification()
    {
        var ready = -1;
        using (TenantScope.BeginEmpty())
        {
            await _fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.Things.Add(HostThing.Create());
                await db.SaveChangesAsync();
                ready = sp.GetRequiredService<IDomainEventCollector>().ReadyCount;
            });
        }

        ready.Should().Be(0);
        (await _fx.HostRows()).Should().BeEmpty();
    }
}
