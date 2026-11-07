using System.Security.Claims;
using RedisOptions = Shared.Core.Options.RedisOptions;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Notification.Contracts.Realtime;
using StackExchange.Redis;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Notification.Realtime;

/// <summary>What to close: every connection of <paramref name="UserId"/> (all sessions) or those of the listed sessions.</summary>
public sealed record HubRevocation(Guid UserId, Guid[]? SessionIds, bool AllSessions);

/// <summary>
/// Closes the local hub connections of revoked sessions: tells the client first (<c>ForceDisconnect</c>, so it stops
/// reconnecting with a dead token), then aborts the socket server-side (a client that ignores the message is cut anyway).
/// </summary>
public sealed class HubSessionCloser(
    HubConnectionRegistry registry,
    IHubContext<NotificationHub, INotificationClient> hub,
    NotificationMetrics metrics,
    ILogger<HubSessionCloser> logger)
{
    /// <summary>Returns how many local connections were closed.</summary>
    public async Task<int> CloseAsync(HubRevocation revocation)
    {
        var targets = registry.Find(revocation.UserId, revocation.SessionIds, revocation.AllSessions);
        foreach (var connection in targets)
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
            finally
            {
                connection.Context.Abort();
            }
        }

        if (targets.Count > 0)
        {
            logger.LogInformation("Closed {Count} hub connection(s) of user {UserId} after session revocation",
                targets.Count, revocation.UserId);
        }

        return targets.Count;
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
/// publishes; EVERY replica, including the publisher, is subscribed and closes its own matching sockets. If publishing
/// fails the local sockets are still closed and the rest fall back to token expiry and the per-invocation check.
/// </summary>
public sealed class RedisHubRevocationBroadcaster(
    IConnectionMultiplexer redis,
    HubSessionCloser closer,
    IOptions<RedisOptions> redisOptions,
    ILogger<RedisHubRevocationBroadcaster> logger) : IHubRevocationBroadcaster, IHostedService
{
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
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().SubscribeAsync(Channel, (channel, message) => { _ = HandleAsync(message.ToString()); });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Subscribing to hub revocations failed; sockets of this replica close at token expiry");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().UnsubscribeAsync(Channel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Unsubscribing from hub revocations failed");
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
/// Defense in depth: re-checks the session's revocation on connect and on every hub invocation (the hub has none today,
/// so this guards any future method), beyond the handshake check and <c>CloseOnAuthenticationExpiration</c>. Cheap
/// (one store lookup). Fails open when the store is down: the other layers still apply.
/// </summary>
public sealed class SessionRevocationHubFilter(ILogger<SessionRevocationHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (await IsRevokedAsync(invocationContext.Context, invocationContext.ServiceProvider))
            throw new HubException("session_revoked");

        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (await IsRevokedAsync(context.Context, context.ServiceProvider))
            return;

        await next(context);
    }

    private async Task<bool> IsRevokedAsync(HubCallerContext caller, IServiceProvider services)
    {
        var checker = services.GetService<ISessionRevocationChecker>();
        if (checker is null || HubConnectionRegistry.SessionIdOf(caller.User) is not { } sessionId)
            return false;

        try
        {
            if (!await checker.IsRevokedAsync(sessionId, caller.ConnectionAborted))
                return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Session revocation check failed; allowing the hub call");
            return false;
        }

        caller.Abort();
        return true;
    }
}
