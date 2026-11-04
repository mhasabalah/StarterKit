using Shared.Contracts.Mediation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;

namespace Shared.Core.Tests.Events;

/// <summary>Pure unit tests of the collector's stage → (transaction) → ready lifecycle and the dispatcher loop.</summary>
public sealed class DomainEventCollectorTests
{
    private sealed record Ping(int N) : INotification;

    private readonly DomainEventCollector _collector = new();
    private readonly object _context = new();
    private readonly object _transaction = new();

    [Fact]
    public void Staged_events_are_ready_only_after_the_save_succeeds()
    {
        _collector.Stage(_context, [new Ping(1)]);
        _collector.ReadyCount.Should().Be(0);

        _collector.SaveSucceeded(_context, openTransaction: null);

        _collector.DequeueAll().Should().ContainSingle().Which.Should().Be(new Ping(1));
        _collector.ReadyCount.Should().Be(0);
    }

    [Fact]
    public void Failed_save_discards_only_its_own_staged_events()
    {
        var other = new object();
        _collector.Stage(_context, [new Ping(1)]);
        _collector.Stage(other, [new Ping(2)]);

        _collector.SaveFailed(_context);
        _collector.SaveSucceeded(_context, null); // nothing left to promote
        _collector.SaveSucceeded(other, null);

        _collector.DequeueAll().Should().Equal(new Ping(2));
    }

    [Fact]
    public void Events_saved_in_a_transaction_follow_the_transaction_outcome()
    {
        _collector.Stage(_context, [new Ping(1)]);
        _collector.SaveSucceeded(_context, _transaction);
        _collector.ReadyCount.Should().Be(0);

        _collector.TransactionCommitted(new object()); // some other transaction
        _collector.ReadyCount.Should().Be(0);

        _collector.TransactionCommitted(_transaction);
        _collector.DequeueAll().Should().Equal(new Ping(1));

        _collector.Stage(_context, [new Ping(2)]);
        _collector.SaveSucceeded(_context, _transaction);
        _collector.TransactionRolledBack(_transaction);
        _collector.TransactionCommitted(_transaction);
        _collector.DequeueAll().Should().BeEmpty();
    }

    [Fact]
    public void Clear_discards_every_stage()
    {
        _collector.Stage(_context, [new Ping(1)]);
        _collector.Stage(new object(), [new Ping(2)]);
        _collector.SaveSucceeded(_context, _transaction);
        _collector.Collect([new Ping(3)]);

        _collector.Clear();
        _collector.TransactionCommitted(_transaction);

        _collector.DequeueAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_keeps_draining_until_handlers_stop_raising_events()
    {
        var published = new List<INotification>();
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) =>
            {
                var ping = (Ping)e;
                published.Add(ping);
                if (ping.N < 3)
                    _collector.Collect([new Ping(ping.N + 1)]); // handler raised (and committed) a new event
            })
            .Returns(Task.CompletedTask);

        _collector.Collect([new Ping(1)]);
        await new DomainEventDispatcher(_collector, publisher.Object, NullLogger<DomainEventDispatcher>.Instance)
            .DispatchPendingAsync();

        published.Should().Equal(new Ping(1), new Ping(2), new Ping(3));
        _collector.ReadyCount.Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_stops_an_endless_event_cycle()
    {
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) => _collector.Collect([new Ping(((Ping)e).N + 1)]))
            .Returns(Task.CompletedTask);

        _collector.Collect([new Ping(1)]);
        var dispatch = () => new DomainEventDispatcher(_collector, publisher.Object, NullLogger<DomainEventDispatcher>.Instance)
            .DispatchPendingAsync();

        await dispatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not settle*");
        publisher.Invocations.Should().HaveCount(DomainEventDispatcher.MaxRounds);
    }

    [Fact]
    public async Task Dispatcher_raises_the_depth_while_publishing_so_nested_sends_do_not_dispatch()
    {
        var depthSeen = 0;
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback(() => depthSeen = _collector.IncrementDepth() - 1)
            .Returns(Task.CompletedTask);

        _collector.Collect([new Ping(1)]);
        await new DomainEventDispatcher(_collector, publisher.Object, NullLogger<DomainEventDispatcher>.Instance)
            .DispatchPendingAsync();

        depthSeen.Should().Be(1, "a Send inside a handler would see depth 2 and therefore not dispatch");
    }
}
