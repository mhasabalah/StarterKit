using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Notification.Contracts.Realtime;
using StackExchange.Redis;
using UserDirectory.Contracts.Sessions;
using UserDirectory.Contracts.Users.IntegrationEvents;
using RedisOptions = Shared.Core.Options.RedisOptions;

namespace Notification.Realtime;

/// <summary>What to close: every connection of <paramref name="UserId"/> (all sessions) or those of the listed sessions.</summary>
public sealed record HubRevocation(Guid UserId, Guid[]? SessionIds, bool AllSessions);

/// <summary>
/// Closes the local hub connections of revoked sessions: tells every client first (<c>ForceDisconnect</c>, so it stops
/// reconnecting with a dead token), gives the transports <see cref="DeliveryGrace"/> to deliver it, then aborts the
/// sockets server-side (a client that ignores the message is cut anyway; the abort always happens).
/// </summary>
public sealed class HubSessionCloser(
    HubConnectionRegistry registry,
    IHubContext<NotificationHub, INotificationClient> hub,
    NotificationMetrics metrics,
    ILogger<HubSessionCloser> logger,
    TimeProvider? timeProvider = null)
{
    /// <summary>
    /// How long the transports get to deliver <c>ForceDisconnect</c> before the sockets are aborted. A send completes
    /// when the message is queued on the connection, not when the client has it; aborting at once can drop it.
    /// </summary>
    public static readonly TimeSpan DeliveryGrace = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Returns how many local connections were closed.</summary>
    public async Task<int> CloseAsync(HubRevocation revocation)
    {
        var targets = registry.Find(revocation.UserId, revocation.SessionIds, revocation.AllSessions);
        if (targets.Count == 0)
            return 0;

        try
        {
            await Task.WhenAll(targets.Select(TellAsync));
            await Task.Delay(DeliveryGrace, _time);
        }
        finally
        {
            foreach (var connection in targets)
                connection.Context.Abort();
        }

        logger.LogInformation("Closed {Count} hub connection(s) of user {UserId} after session revocation",
            targets.Count, revocation.UserId);
        return targets.Count;
    }

    private async Task TellAsync(TrackedHubConnection connection)
    {
        try
        {
            await hub.Clients.Client(connection.ConnectionId)
                .ForceDisconnect(new ForceDisconnectEvent(ForceDisconnectEvent.SessionRevoked));
            metrics.RealtimeSent(NotificationHubContract.ForceDisconnect, success: true);
        }
        catch (Exception ex)
        {
            metrics.RealtimeSent(NotificationHubContract.ForceDisconnect, success: false);
            logger.LogDebug(ex, "ForceDisconnect to {ConnectionId} failed; aborting anyway", connection.ConnectionId);
        }
    }
}

/// <summary>
/// Delivers a revocation to the hub connections of EVERY replica. The integration event reaches one replica (competing
/// consumers), but the sockets are spread over all of them.
/// </summary>
public interface IHubRevocationBroadcaster
{
    Task BroadcastAsync(HubRevocation revocation, CancellationToken ct = default);
}

/// <summary>Single instance (no Redis): closing locally is enough.</summary>
public sealed class LocalHubRevocationBroadcaster(HubSessionCloser closer) : IHubRevocationBroadcaster
{
    public async Task BroadcastAsync(HubRevocation revocation, CancellationToken ct = default) =>
        await closer.CloseAsync(revocation);
}

/// <summary>Realtime disabled: there is no hub, so nothing to close.</summary>
public sealed class NoOpHubRevocationBroadcaster : IHubRevocationBroadcaster
{
    public Task BroadcastAsync(HubRevocation revocation, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Redis pub/sub fan-out (same approach as the tenant L1 cache invalidation bus): the replica that consumed the event
/// publishes; every SUBSCRIBED replica, including the publisher, closes its own matching sockets.
/// <para>
/// Degraded behavior (Redis down or this replica not subscribed): sockets of this replica are always closed locally
/// (publish failure, or publish succeeded while this replica is not subscribed); sockets on OTHER replicas then close at
/// access-token expiry (<c>CloseOnAuthenticationExpiration</c>) or on their next revocation check. A failed subscription
/// is retried in the background (and after a reconnect), so the replica rejoins the fan-out without a restart.
/// </para>
/// </summary>
public sealed class RedisHubRevocationBroadcaster(
    IConnectionMultiplexer redis,
    HubSessionCloser closer,
    IOptions<RedisOptions> redisOptions,
    ILogger<RedisHubRevocationBroadcaster> logger) : IHubRevocationBroadcaster, IHostedService, IDisposable
{
    private CancellationTokenSource? _stop;
    private Task? _retryLoop;
    private volatile bool _subscribed;

    /// <summary>How often a missing subscription is retried.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public bool IsSubscribed => _subscribed;

    private RedisChannel Channel =>
        RedisChannel.Literal(redisOptions.Value.InstanceName.TrimEnd(':') + ":notification:hub-revoke");

    public async Task BroadcastAsync(HubRevocation revocation, CancellationToken ct = default)
    {
        try
        {
            await redis.GetSubscriber().PublishAsync(Channel, JsonSerializer.Serialize(revocation));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Broadcasting the hub revocation of user {UserId} failed; closing local sockets only", revocation.UserId);
            await closer.CloseAsync(revocation);
            return;
        }

        if (!_subscribed)
        {
            // Published, but we will not hear our own message: do our own part.
            await closer.CloseAsync(revocation);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionRestored += OnConnectionRestored;
        await TrySubscribeAsync();
        _stop = new CancellationTokenSource();
        _retryLoop = RetryUntilStoppedAsync(_stop.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionRestored -= OnConnectionRestored;
        if (_stop is not null)
        {
            await _stop.CancelAsync();
            if (_retryLoop is not null)
                await _retryLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        try
        {
            await redis.GetSubscriber().UnsubscribeAsync(Channel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Unsubscribing from hub revocations failed");
        }

        _subscribed = false;
    }

    public void Dispose() => _stop?.Dispose();

    /// <summary>One subscription attempt; returns whether this replica is subscribed afterwards.</summary>
    public async Task<bool> TrySubscribeAsync()
    {
        try
        {
            await redis.GetSubscriber().SubscribeAsync(Channel, (channel, message) => { _ = HandleAsync(message.ToString()); });
            _subscribed = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _subscribed = false;
            logger.LogWarning(ex, "Subscribing to hub revocations failed; retrying in {Seconds}s (sockets of this replica close locally or at token expiry meanwhile)",
                RetryInterval.TotalSeconds);
        }

        return _subscribed;
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        // Redis re-establishes subscriptions itself; this makes sure and covers a subscribe that failed during the outage.
        _subscribed = false;
        _ = TrySubscribeAsync();
    }

    private async Task RetryUntilStoppedAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(RetryInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                if (!_subscribed)
                    await TrySubscribeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    /// <summary>Handles one received message; malformed ones are ignored (logged).</summary>
    public async Task HandleAsync(string payload)
    {
        try
        {
            var revocation = JsonSerializer.Deserialize<HubRevocation>(payload);
            if (revocation is not null)
                await closer.CloseAsync(revocation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ignoring malformed hub revocation message");
        }
    }
}

/// <summary>Consumes <see cref="UserSessionsRevokedIntegrationEvent"/>: closes the revoked sessions' sockets on all replicas. Idempotent.</summary>
public sealed class UserSessionsRevokedConsumer(IHubRevocationBroadcaster broadcaster) : IConsumer<UserSessionsRevokedIntegrationEvent>
{
    public Task Consume(ConsumeContext<UserSessionsRevokedIntegrationEvent> context)
    {
        var message = context.Message;
        return broadcaster.BroadcastAsync(
            new HubRevocation(message.UserId, message.SessionIds?.ToArray(), message.AllSessions),
            context.CancellationToken);
    }
}

/// <summary>
/// Tracks every connection in the <see cref="HubConnectionRegistry"/> and re-checks the session's revocation when it
/// connects (<see cref="ISessionRevocationChecker"/>: the session id AND the security-stamp hash, so a revoke-all that
/// rotated the stamp is caught). The connection is REGISTERED FIRST and checked second: a revocation that lands in
/// between finds the socket in the registry, one that landed before is caught by the check. The hub has no client
/// methods, so there is no per-invocation check; beyond this and the handshake check, <c>CloseOnAuthenticationExpiration</c>
/// bounds a missed revocation to one access-token lifetime. Fails open when the store is down.
/// </summary>
public sealed class SessionRevocationHubFilter(HubConnectionRegistry registry, ILogger<SessionRevocationHubFilter> logger) : IHubFilter
{
    /// <summary>The security-stamp hash claim of Identity access tokens (<c>IdentityClaimTypes.SecurityStampHash</c>).</summary>
    public const string SecurityStampHashClaim = "sstamp";

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var caller = context.Context;
        registry.Add(caller);

        if (await IsRevokedAsync(caller, context.ServiceProvider))
        {
            registry.Remove(caller.ConnectionId);
            caller.Abort();
            return;
        }

        await next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        registry.Remove(context.Context.ConnectionId);
        return next(context, exception);
    }

    private async Task<bool> IsRevokedAsync(HubCallerContext caller, IServiceProvider services)
    {
        var checker = services.GetService<ISessionRevocationChecker>();
        var user = caller.User;
        if (checker is null || !Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return false;

        try
        {
            return await checker.IsRevokedAsync(
                userId, HubConnectionRegistry.SessionIdOf(user), user?.FindFirst(SecurityStampHashClaim)?.Value,
                caller.ConnectionAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Session revocation check failed; allowing the hub connection");
            return false;
        }
    }
}
