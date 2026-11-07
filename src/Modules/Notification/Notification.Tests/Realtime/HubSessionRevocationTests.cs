using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts.Realtime;
using Shared.Core.Authorization;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Notification.Tests.Realtime;

/// <summary>
/// Session revocation closes live hub sockets: the registry/closer over the real hub (only the revoked sid closes),
/// the consumer's mapping, and the per-invocation revocation filter.
/// </summary>
public sealed class HubSessionRevocationTests : IAsyncLifetime
{
    private Microsoft.AspNetCore.Builder.WebApplication _app = null!;
    private Microsoft.AspNetCore.TestHost.TestServer _server = null!;

    public async Task InitializeAsync() => (_app, _server) = await NotificationHubTests.StartHostAsync(redisConnection: null);

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static async Task<TaskCompletionSource> ClosedSignal(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await Task.CompletedTask;
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
        var firstClosed = await ClosedSignal(first);
        await Task.WhenAll(first.StartAsync(), second.StartAsync());

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
        var c1 = await ClosedSignal(a1);
        var c2 = await ClosedSignal(a2);
        await Task.WhenAll(a1.StartAsync(), a2.StartAsync(), b1.StartAsync());

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
        registry.Count.Should().Be(1);

        await connection.StopAsync();
        await connection.DisposeAsync();

        await Task.Delay(300);
        registry.Count.Should().Be(0);
    }

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

    private static (HubLifetimeContext Context, Mock<HubCallerContext> Caller) Lifetime(Guid? sid, ISessionRevocationChecker? checker)
    {
        var caller = new Mock<HubCallerContext>();
        var claims = sid is { } id ? new[] { new Claim("sid", id.ToString()) } : [];
        caller.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
        caller.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        var services = new ServiceCollection();
        if (checker is not null)
            services.AddSingleton(checker);
        var hub = new NotificationHub(new HubConnectionRegistry(), NullLogger<NotificationHub>.Instance);
        return (new HubLifetimeContext(caller.Object, services.BuildServiceProvider(), hub), caller);
    }

    [Fact]
    public async Task The_filter_aborts_a_revoked_session_and_skips_the_hub()
    {
        var sid = Guid.NewGuid();
        var checker = new Mock<ISessionRevocationChecker>();
        checker.Setup(c => c.IsRevokedAsync(sid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var (context, caller) = Lifetime(sid, checker.Object);
        var nextCalled = false;

        await new SessionRevocationHubFilter(NullLogger<SessionRevocationHubFilter>.Instance)
            .OnConnectedAsync(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

        nextCalled.Should().BeFalse();
        caller.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task The_filter_lets_a_live_session_through_and_fails_open_when_the_store_is_down()
    {
        var sid = Guid.NewGuid();
        var live = new Mock<ISessionRevocationChecker>();
        live.Setup(c => c.IsRevokedAsync(sid, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var down = new Mock<ISessionRevocationChecker>();
        down.Setup(c => c.IsRevokedAsync(sid, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("redis down"));

        foreach (var checker in new[] { live.Object, down.Object, null })
        {
            var (context, caller) = Lifetime(sid, checker);
            var nextCalled = false;
            await new SessionRevocationHubFilter(NullLogger<SessionRevocationHubFilter>.Instance)
                .OnConnectedAsync(context, _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                });
            nextCalled.Should().BeTrue();
            caller.Verify(c => c.Abort(), Times.Never);
        }
    }
}
