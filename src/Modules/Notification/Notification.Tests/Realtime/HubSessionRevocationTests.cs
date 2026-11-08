using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Contracts.Realtime;
using StackExchange.Redis;
using Testcontainers.Redis;
using UserDirectory.Contracts.Sessions;
using UserDirectory.Contracts.Users.IntegrationEvents;
using RedisOptions = Shared.Core.Options.RedisOptions;

namespace Notification.Tests.Realtime;

internal static class Poll
{
    public static async Task UntilAsync(Func<bool> condition, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(25);
        }
    }
}

/// <summary>
/// Session revocation closes live hub sockets: the registry/closer over the real hub (only the revoked sid closes),
/// the consumer's mapping, the connect-time revocation filter and the registry index.
/// </summary>
public sealed class HubSessionRevocationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync() => (_app, _server) = await NotificationHubTests.StartHostAsync(redisConnection: null);

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// The client's StartAsync completes before the server's OnConnectedAsync has registered the connection (it is
    /// visible on slow CI runners): wait for the registry before revoking, or the closer finds nothing to close.
    /// </summary>
    private Task RegisteredAsync(Guid userId, int connections) =>
        Poll.UntilAsync(() =>
            _app.Services.GetRequiredService<HubConnectionRegistry>().Find(userId, null, allSessions: true).Count == connections);

    internal static TaskCompletionSource ClosedSignal(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        return closed;
    }

    [Fact]
    public async Task Revoking_one_session_closes_only_that_sessions_socket_and_tells_the_client()
    {
        Guid alice = Guid.NewGuid(), sid1 = Guid.NewGuid(), sid2 = Guid.NewGuid();
        await using var first = NotificationHubTests.Connect(_server, alice, sessionId: sid1);
        await using var second = NotificationHubTests.Connect(_server, alice, sessionId: sid2);
        var forced = new TaskCompletionSource<ForceDisconnectEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.On<ForceDisconnectEvent>(NotificationHubContract.ForceDisconnect, e => forced.TrySetResult(e));
        var firstClosed = ClosedSignal(first);
        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        await RegisteredAsync(alice, 2);

        var closer = _app.Services.GetRequiredService<HubSessionCloser>();
        var closedCount = await closer.CloseAsync(new HubRevocation(alice, [sid1], AllSessions: false));

        closedCount.Should().Be(1);
        (await forced.Task.WaitAsync(TimeSpan.FromSeconds(10))).Reason.Should().Be(ForceDisconnectEvent.SessionRevoked);
        await firstClosed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        first.State.Should().Be(HubConnectionState.Disconnected);
        second.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Revoking_all_sessions_closes_every_socket_of_that_user_and_nobody_elses()
    {
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid();
        await using var a1 = NotificationHubTests.Connect(_server, alice, sessionId: Guid.NewGuid());
        await using var a2 = NotificationHubTests.Connect(_server, alice, sessionId: Guid.NewGuid());
        await using var b1 = NotificationHubTests.Connect(_server, bob, sessionId: Guid.NewGuid());
        var c1 = ClosedSignal(a1);
        var c2 = ClosedSignal(a2);
        await Task.WhenAll(a1.StartAsync(), a2.StartAsync(), b1.StartAsync());
        await RegisteredAsync(alice, 2);
        await RegisteredAsync(bob, 1);

        await _app.Services.GetRequiredService<HubSessionCloser>().CloseAsync(new HubRevocation(alice, null, AllSessions: true));

        await Task.WhenAll(c1.Task, c2.Task).WaitAsync(TimeSpan.FromSeconds(10));
        b1.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Disconnected_clients_leave_the_registry()
    {
        var registry = _app.Services.GetRequiredService<HubConnectionRegistry>();
        var connection = NotificationHubTests.Connect(_server, Guid.NewGuid(), sessionId: Guid.NewGuid());
        await connection.StartAsync();
        await Poll.UntilAsync(() => registry.Count == 1);

        await connection.StopAsync();
        await connection.DisposeAsync();

        await Poll.UntilAsync(() => registry.Count == 0);
    }

    // ---- registry ---------------------------------------------------------------------------------------------

    private static HubCallerContext Caller(Guid userId, Guid? sid, string connectionId)
    {
        var caller = new Mock<HubCallerContext>();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (sid is { } id)
            claims.Add(new Claim("sid", id.ToString()));
        caller.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
        caller.SetupGet(c => c.UserIdentifier).Returns(userId.ToString("D"));
        caller.SetupGet(c => c.ConnectionId).Returns(connectionId);
        caller.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        return caller.Object;
    }

    [Fact]
    public void The_registry_finds_by_user_and_session_and_forgets_removed_connections()
    {
        var registry = new HubConnectionRegistry();
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid(), sid1 = Guid.NewGuid(), sid2 = Guid.NewGuid();
        registry.Add(Caller(alice, sid1, "a1"));
        registry.Add(Caller(alice, sid2, "a2"));
        registry.Add(Caller(alice, null, "a3"));
        registry.Add(Caller(bob, sid1, "b1"));

        registry.Find(alice, [sid1], false).Select(c => c.ConnectionId).Should().Equal("a1");
        registry.Find(alice, null, true).Select(c => c.ConnectionId).Should().BeEquivalentTo("a1", "a2", "a3");
        registry.Find(Guid.NewGuid(), null, true).Should().BeEmpty();

        registry.Remove("a1");
        registry.Remove("unknown");

        registry.Find(alice, [sid1], false).Should().BeEmpty();
        registry.Count.Should().Be(3);
    }

    // ---- consumer ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_consumer_hands_the_event_to_the_broadcaster()
    {
        var broadcaster = new Mock<IHubRevocationBroadcaster>();
        var user = Guid.NewGuid();
        var sid = Guid.NewGuid();
        var context = new Mock<ConsumeContext<UserSessionsRevokedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserSessionsRevokedIntegrationEvent { UserId = user, SessionIds = [sid] });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await new UserSessionsRevokedConsumer(broadcaster.Object).Consume(context.Object);

        broadcaster.Verify(b => b.BroadcastAsync(
            It.Is<HubRevocation>(r => r.UserId == user && !r.AllSessions && r.SessionIds!.Single() == sid),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- hub filter -------------------------------------------------------------------------------------------

    private static HubLifetimeContext Lifetime(HubCallerContext caller, ISessionRevocationChecker? checker)
    {
        var services = new ServiceCollection();
        if (checker is not null)
            services.AddSingleton(checker);
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance);
        return new HubLifetimeContext(caller, services.BuildServiceProvider(), hub);
    }

    private static SessionRevocationHubFilter Filter(HubConnectionRegistry registry) =>
        new(registry, NullLogger<SessionRevocationHubFilter>.Instance);

    [Fact]
    public async Task The_filter_registers_first_then_aborts_a_revoked_session_removes_it_and_skips_the_hub()
    {
        Guid user = Guid.NewGuid(), sid = Guid.NewGuid();
        var registry = new HubConnectionRegistry();
        var wasRegisteredDuringCheck = false;
        var checker = new Mock<ISessionRevocationChecker>();
        checker.Setup(c => c.IsRevokedAsync(user, sid, "stamp", It.IsAny<CancellationToken>()))
            .Callback(() => wasRegisteredDuringCheck = registry.Count == 1)
            .ReturnsAsync(true);
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("sid", sid.ToString()), new Claim("sstamp", "stamp")], "t")));
        caller.SetupGet(c => c.UserIdentifier).Returns(user.ToString("D"));
        caller.SetupGet(c => c.ConnectionId).Returns("c1");
        caller.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        var nextCalled = false;

        await Filter(registry).OnConnectedAsync(Lifetime(caller.Object, checker.Object), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        wasRegisteredDuringCheck.Should().BeTrue("a revocation landing between check and registration would otherwise be missed");
        nextCalled.Should().BeFalse();
        caller.Verify(c => c.Abort(), Times.Once);
        registry.Count.Should().Be(0);
    }

    [Fact]
    public async Task The_filter_keeps_a_live_session_registered_and_fails_open_when_the_store_is_down_or_missing()
    {
        Guid user = Guid.NewGuid(), sid = Guid.NewGuid();
        var live = new Mock<ISessionRevocationChecker>();
        live.Setup(c => c.IsRevokedAsync(user, sid, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var down = new Mock<ISessionRevocationChecker>();
        down.Setup(c => c.IsRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("redis down"));

        foreach (var checker in new[] { live.Object, down.Object, null })
        {
            var registry = new HubConnectionRegistry();
            var caller = Mock.Get(Caller(user, sid, "c1"));
            var nextCalled = false;

            await Filter(registry).OnConnectedAsync(Lifetime(caller.Object, checker), _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            nextCalled.Should().BeTrue();
            caller.Verify(c => c.Abort(), Times.Never);
            registry.Count.Should().Be(1);
        }
    }

    [Fact]
    public async Task The_filter_forgets_the_connection_on_disconnect()
    {
        var registry = new HubConnectionRegistry();
        var caller = Caller(Guid.NewGuid(), Guid.NewGuid(), "c1");
        registry.Add(caller);

        await Filter(registry).OnDisconnectedAsync(Lifetime(caller, null), null, (_, _) => Task.CompletedTask);

        registry.Count.Should().Be(0);
    }
}

/// <summary>The Redis fan-out: a revocation published on one replica closes sockets held by another.</summary>
public sealed class HubRevocationRedisTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private WebApplication _replicaA = null!;
    private WebApplication _replicaB = null!;
    private TestServer _serverA = null!;
    private TestServer _serverB = null!;

    private static Action<IServiceCollection> UseRedisBroadcaster(string connection) => services =>
    {
        services.Configure<RedisOptions>(o => o.InstanceName = "hubtest:");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connection));
        services.AddSingleton<RedisHubRevocationBroadcaster>();
        services.RemoveAll<IHubRevocationBroadcaster>();
        services.AddSingleton<IHubRevocationBroadcaster>(sp => sp.GetRequiredService<RedisHubRevocationBroadcaster>());
        services.AddHostedService(sp => sp.GetRequiredService<RedisHubRevocationBroadcaster>());
    };

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        var connection = _redis.GetConnectionString();
        (_replicaA, _serverA) = await NotificationHubTests.StartHostAsync(null, extraServices: UseRedisBroadcaster(connection));
        (_replicaB, _serverB) = await NotificationHubTests.StartHostAsync(null, extraServices: UseRedisBroadcaster(connection));
    }

    public async Task DisposeAsync()
    {
        await _replicaA.DisposeAsync();
        await _replicaB.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task A_revocation_broadcast_on_one_replica_closes_the_sockets_on_both_replicas_but_only_the_revoked_session()
    {
        Guid alice = Guid.NewGuid(), revoked = Guid.NewGuid(), other = Guid.NewGuid();
        await using var onA = NotificationHubTests.Connect(_serverA, alice, sessionId: revoked);
        await using var onB = NotificationHubTests.Connect(_serverB, alice, sessionId: revoked);
        await using var keptOnB = NotificationHubTests.Connect(_serverB, alice, sessionId: other);
        var closedA = HubSessionRevocationTests.ClosedSignal(onA);
        var closedB = HubSessionRevocationTests.ClosedSignal(onB);
        await Task.WhenAll(onA.StartAsync(), onB.StartAsync(), keptOnB.StartAsync());

        await _replicaA.Services.GetRequiredService<IHubRevocationBroadcaster>()
            .BroadcastAsync(new HubRevocation(alice, [revoked], AllSessions: false));

        await Task.WhenAll(closedA.Task, closedB.Task).WaitAsync(TimeSpan.FromSeconds(10));
        keptOnB.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task A_malformed_message_is_ignored_and_a_valid_one_afterwards_still_works()
    {
        var user = Guid.NewGuid();
        await using var client = NotificationHubTests.Connect(_serverA, user, sessionId: Guid.NewGuid());
        var closed = HubSessionRevocationTests.ClosedSignal(client);
        await client.StartAsync();
        var broadcaster = _replicaA.Services.GetRequiredService<RedisHubRevocationBroadcaster>();

        await broadcaster.HandleAsync("{ not json");
        client.State.Should().Be(HubConnectionState.Connected);

        await broadcaster.BroadcastAsync(new HubRevocation(user, null, AllSessions: true));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

/// <summary>Degraded Redis behavior of the broadcaster, with a stubbed multiplexer.</summary>
public sealed class RedisHubRevocationBroadcasterTests
{
    private static (RedisHubRevocationBroadcaster Broadcaster, Mock<ISubscriber> Subscriber, HubConnectionRegistry Registry, Mock<HubCallerContext> Socket, Guid UserId)
        Create(TimeSpan? retry = null)
    {
        var subscriber = new Mock<ISubscriber>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);

        var userId = Guid.NewGuid();
        var registry = new HubConnectionRegistry();
        var socket = new Mock<HubCallerContext>();
        socket.SetupGet(c => c.UserIdentifier).Returns(userId.ToString("D"));
        socket.SetupGet(c => c.ConnectionId).Returns("c1");
        socket.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        registry.Add(socket.Object);

        var client = new Mock<INotificationClient>();
        var clients = new Mock<IHubClients<INotificationClient>>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<NotificationHub, INotificationClient>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var closer = new HubSessionCloser(registry, hub.Object, new NotificationMetrics(), NullLogger<HubSessionCloser>.Instance);

        var broadcaster = new RedisHubRevocationBroadcaster(redis.Object, closer,
            Microsoft.Extensions.Options.Options.Create(new RedisOptions()), NullLogger<RedisHubRevocationBroadcaster>.Instance)
        {
            RetryInterval = retry ?? TimeSpan.FromMilliseconds(30)
        };
        return (broadcaster, subscriber, registry, socket, userId);
    }

    [Fact]
    public async Task A_failed_subscription_is_retried_in_the_background_until_it_works()
    {
        var (broadcaster, subscriber, _, _, _) = Create();
        var attempts = 0;
        subscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(() => ++attempts < 3 ? Task.FromException(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")) : Task.CompletedTask);

        await broadcaster.StartAsync(CancellationToken.None);
        broadcaster.IsSubscribed.Should().BeFalse();

        await Poll.UntilAsync(() => broadcaster.IsSubscribed);
        attempts.Should().Be(3);
        await broadcaster.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task When_the_publish_succeeds_but_this_replica_is_not_subscribed_it_closes_its_own_sockets()
    {
        var (broadcaster, subscriber, _, socket, userId) = Create(TimeSpan.FromHours(1));
        subscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.FromException(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));
        subscriber.Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>())).ReturnsAsync(0);
        await broadcaster.StartAsync(CancellationToken.None);

        await broadcaster.BroadcastAsync(new HubRevocation(userId, null, AllSessions: true));

        socket.Verify(c => c.Abort(), Times.Once);
        await broadcaster.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task When_publishing_fails_the_local_sockets_are_still_closed()
    {
        var (broadcaster, subscriber, _, socket, userId) = Create(TimeSpan.FromHours(1));
        subscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        subscriber.Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
        await broadcaster.StartAsync(CancellationToken.None);

        await broadcaster.BroadcastAsync(new HubRevocation(userId, null, AllSessions: true));

        socket.Verify(c => c.Abort(), Times.Once);
        await broadcaster.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task When_subscribed_the_publisher_leaves_closing_to_the_subscription_so_nothing_closes_twice()
    {
        var (broadcaster, subscriber, _, socket, userId) = Create(TimeSpan.FromHours(1));
        subscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        subscriber.Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>())).ReturnsAsync(1);
        await broadcaster.StartAsync(CancellationToken.None);

        await broadcaster.BroadcastAsync(new HubRevocation(userId, null, AllSessions: true));

        socket.Verify(c => c.Abort(), Times.Never);
        await broadcaster.StopAsync(CancellationToken.None);
    }
}
