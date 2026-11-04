using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;

namespace Shared.Core.Tests.Events;

/// <summary>
/// Domain-event delivery rules, end to end against real PostgreSQL with the real interceptor, collector, dispatcher,
/// mediator behavior and <see cref="IUnitOfWorkRunner"/>:
/// <list type="bullet">
/// <item>published exactly once, after the OUTERMOST unit of work commits (nested Send included);</item>
/// <item>events of failed saves / rolled-back transactions / failed units of work are discarded and never leak into
/// the next command in the same scope;</item>
/// <item>events raised by event handlers are published in the same drain;</item>
/// <item>background work (jobs) publishes through the runner - after success only.</item>
/// </list>
/// </summary>
[Collection(DomainEventCollection.Name)]
public sealed class DomainEventDispatchTests(DomainEventFixture fx)
{
    private PublishedEvents Published => fx.Published;

    // ----- mediator path -----

    [Fact]
    public async Task Nested_send_dispatches_once_after_the_outermost_command_commits()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateGadgetCommand(
            DomainEventFixture.UniqueName("outer"), InnerName: DomainEventFixture.UniqueName("inner")));

        result.PublishedDuringHandler.Should().Be(0, "nothing is published until the root command completes");
        Published.For(result.Id).Should().ContainSingle().Which.Should().BeOfType<GadgetCreatedEvent>();
        Published.For(result.InnerId!.Value).Should().ContainSingle().Which.Should().BeOfType<GadgetCreatedEvent>();
        scope.ServiceProvider.GetRequiredService<IDomainEventCollector>().ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Event_raised_by_an_event_handler_is_dispatched_in_the_same_drain()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateGadgetCommand(Gadget.ChainPrefix + Guid.NewGuid().ToString("N")));

        var events = Published.For(result.Id);
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<GadgetCreatedEvent>();
        events[1].Should().BeOfType<GadgetRenamedEvent>();
    }

    [Fact]
    public async Task Failed_save_discards_its_events_and_they_never_leak_into_the_next_command()
    {
        var taken = DomainEventFixture.UniqueName("taken");
        await using (var setup = fx.Services.CreateAsyncScope())
        {
            await setup.ServiceProvider.GetRequiredService<ISender>().Send(new CreateGadgetCommand(taken));
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<IDomainEventCollector>();

        var duplicate = Gadget.Create(taken);
        db.Gadgets.Add(duplicate);
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        collector.ReadyCount.Should().Be(0, "a failed save must not leave events behind");
        db.ChangeTracker.Clear();

        // Next successful command in the SAME scope.
        var next = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateGadgetCommand(DomainEventFixture.UniqueName("next")));

        Published.For(next.Id).Should().ContainSingle();
        Published.For(duplicate.Id).Should().BeEmpty("the duplicate was never committed");
    }

    [Fact]
    public async Task Failed_save_inside_a_command_does_not_leak_into_the_next_command()
    {
        var taken = DomainEventFixture.UniqueName("taken");
        await using var scope = fx.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var first = await sender.Send(new CreateGadgetCommand(taken));

        await FluentActions.Invoking(() => sender.Send(new CreateGadgetCommand(taken)))
            .Should().ThrowAsync<DbUpdateException>();
        scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>().ChangeTracker.Clear();

        var next = await sender.Send(new CreateGadgetCommand(DomainEventFixture.UniqueName("next")));

        Published.For(first.Id).Should().ContainSingle();
        Published.For(next.Id).Should().ContainSingle();
        scope.ServiceProvider.GetRequiredService<IDomainEventCollector>().ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Root_command_that_fails_after_a_save_discards_its_events()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var name = DomainEventFixture.UniqueName("fails-late");

        await FluentActions.Invoking(() => sender.Send(new CreateGadgetCommand(name, FailAfterSave: true)))
            .Should().ThrowAsync<InvalidOperationException>();
        var failedId = await IdOf(name);

        var next = await sender.Send(new CreateGadgetCommand(DomainEventFixture.UniqueName("next")));

        Published.For(next.Id).Should().ContainSingle();
        Published.For(failedId).Should().BeEmpty("a failed unit of work publishes nothing, not even in a later command");
    }

    [Fact]
    public async Task Concurrency_conflict_discards_the_staged_events()
    {
        Guid id;
        await using (var setup = fx.Services.CreateAsyncScope())
        {
            id = (await setup.ServiceProvider.GetRequiredService<ISender>()
                .Send(new CreateGadgetCommand(DomainEventFixture.UniqueName("race")))).Id;
        }

        await using var winnerScope = fx.Services.CreateAsyncScope();
        await using var loserScope = fx.Services.CreateAsyncScope();
        var winnerDb = winnerScope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var winner = await winnerDb.Gadgets.SingleAsync(g => g.Id == id);
        var loser = await loserDb.Gadgets.SingleAsync(g => g.Id == id);

        winner.Rename(DomainEventFixture.UniqueName("winner"));
        await winnerDb.SaveChangesAsync();
        loser.Rename(DomainEventFixture.UniqueName("loser"));

        // The DbContext throws EF's exception unchanged (translation to 409 is the mediator behavior's job).
        await FluentActions.Invoking(() => loserDb.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

        loserScope.ServiceProvider.GetRequiredService<IDomainEventCollector>().ReadyCount.Should().Be(0);
        await loserScope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync();
        await winnerScope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync();

        Published.For(id).OfType<GadgetRenamedEvent>().Should().ContainSingle()
            .Which.Name.Should().Be(winner.Name);
    }

    // ----- Explicit transactions -----

    [Fact]
    public async Task Events_saved_inside_a_transaction_wait_for_commit()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<IDomainEventCollector>();

        var gadget = Gadget.Create(DomainEventFixture.UniqueName("tx-commit"));
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Gadgets.Add(gadget);
            await db.SaveChangesAsync();
            collector.ReadyCount.Should().Be(0, "the transaction has not committed yet");

            await transaction.CommitAsync();
        }

        collector.ReadyCount.Should().Be(1);
        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync();
        Published.For(gadget.Id).Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Events_of_a_rolled_back_transaction_are_discarded(bool explicitRollback)
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<IDomainEventCollector>();

        var gadget = Gadget.Create(DomainEventFixture.UniqueName("tx-rollback"));
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Gadgets.Add(gadget);
            await db.SaveChangesAsync();

            if (explicitRollback)
                await transaction.RollbackAsync();
            // else: disposed without commit = implicit rollback
        }

        collector.ReadyCount.Should().Be(0);
        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync();
        Published.For(gadget.Id).Should().BeEmpty();
        (await IdOrNull(gadget.Name)).Should().BeNull("the row was rolled back too");
    }

    // ----- Background work: IUnitOfWorkRunner -----

    [Fact]
    public async Task Runner_dispatches_after_the_work_succeeds()
    {
        var runner = fx.Services.GetRequiredService<IUnitOfWorkRunner>();
        var gadget = Gadget.Create(DomainEventFixture.UniqueName("job-ok"));
        var publishedDuringWork = -1;

        await runner.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<DomainEventTestDbContext>();
            db.Gadgets.Add(gadget);
            await db.SaveChangesAsync(ct);
            publishedDuringWork = Published.For(gadget.Id).Count;
        });

        publishedDuringWork.Should().Be(0, "events are published after the unit of work, not during it");
        Published.For(gadget.Id).Should().ContainSingle();
    }

    [Fact]
    public async Task Runner_does_not_dispatch_when_the_work_fails()
    {
        var runner = fx.Services.GetRequiredService<IUnitOfWorkRunner>();
        var gadget = Gadget.Create(DomainEventFixture.UniqueName("job-fails"));

        var run = () => runner.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<DomainEventTestDbContext>();
            db.Gadgets.Add(gadget);
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("job step failed after the save");
        });

        await run.Should().ThrowAsync<InvalidOperationException>();
        Published.For(gadget.Id).Should().BeEmpty();
        (await IdOrNull(gadget.Name)).Should().Be(gadget.Id, "the save itself was committed");
    }

    [Fact]
    public async Task Runner_is_the_root_unit_of_work_for_commands_sent_inside_it()
    {
        var runner = fx.Services.GetRequiredService<IUnitOfWorkRunner>();
        var publishedAfterSend = -1;

        var result = await runner.RunAsync(async (services, ct) =>
        {
            var created = await services.GetRequiredService<ISender>()
                .Send(new CreateGadgetCommand(DomainEventFixture.UniqueName("job-send")), ct);
            publishedAfterSend = Published.For(created.Id).Count;
            return created;
        });

        publishedAfterSend.Should().Be(0, "the nested command does not dispatch on its own inside the runner");
        Published.For(result.Id).Should().ContainSingle();
    }

    [Fact]
    public async Task Runner_dispatches_events_raised_by_event_handlers()
    {
        var runner = fx.Services.GetRequiredService<IUnitOfWorkRunner>();
        var gadget = Gadget.Create(Gadget.ChainPrefix + Guid.NewGuid().ToString("N"));

        await runner.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<DomainEventTestDbContext>();
            db.Gadgets.Add(gadget);
            await db.SaveChangesAsync(ct);
        });

        Published.For(gadget.Id).Select(e => e.GetType()).Should()
            .Equal(typeof(GadgetCreatedEvent), typeof(GadgetRenamedEvent));
    }

    [Fact]
    public async Task Dispatcher_used_directly_publishes_committed_events_once()
    {
        // The pattern for a Hangfire job whose scoped dependencies Hangfire injected.
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        var gadget = Gadget.Create(DomainEventFixture.UniqueName("direct"));

        db.Gadgets.Add(gadget);
        await db.SaveChangesAsync();
        await dispatcher.DispatchPendingAsync();
        await dispatcher.DispatchPendingAsync();

        Published.For(gadget.Id).Should().ContainSingle();
    }

    private async Task<Guid> IdOf(string name) =>
        await IdOrNull(name) ?? throw new InvalidOperationException($"Gadget '{name}' not found.");

    private async Task<Guid?> IdOrNull(string name)
    {
        await using var scope = fx.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>().Gadgets
            .Where(g => g.Name == name)
            .Select(g => (Guid?)g.Id)
            .SingleOrDefaultAsync();
    }
}
