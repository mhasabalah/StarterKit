using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Collections.Concurrent;

namespace Notification.Realtime;

/// <summary>
/// The hub connections held by THIS replica (connection id → caller context, user, session). Filled by
/// <see cref="NotificationHub"/> on connect/disconnect; used to close the sockets of revoked sessions. In memory by
/// design: another replica's sockets are reached by <see cref="IHubRevocationBroadcaster"/>, not by sharing this state.
/// </summary>
public sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, TrackedHubConnection> _connections = new();

    public int Count => _connections.Count;

    public void Add(HubCallerContext context)
    {
        if (!Guid.TryParse(context.UserIdentifier, out var userId))
            return;

        _connections[context.ConnectionId] = new TrackedHubConnection(context.ConnectionId, userId, SessionIdOf(context.User), context);
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>The connections of <paramref name="userId"/> that belong to one of <paramref name="sessionIds"/>, or all when <paramref name="allSessions"/>.</summary>
    public IReadOnlyList<TrackedHubConnection> Find(Guid userId, IReadOnlyCollection<Guid>? sessionIds, bool allSessions) =>
        _connections.Values
            .Where(c => c.UserId == userId
                        && (allSessions || (c.SessionId is { } sid && sessionIds is not null && sessionIds.Contains(sid))))
            .ToList();

    /// <summary>The <c>sid</c> claim (refresh-token family) of an access token; null for tokens without a session.</summary>
    public static Guid? SessionIdOf(System.Security.Claims.ClaimsPrincipal? user)
    {
        var claim = user?.FindFirst("sid")?.Value ?? user?.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public sealed record TrackedHubConnection(string ConnectionId, Guid UserId, Guid? SessionId, HubCallerContext Context);
