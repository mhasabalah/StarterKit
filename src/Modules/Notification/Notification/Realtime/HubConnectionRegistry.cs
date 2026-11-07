using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Notification.Realtime;

/// <summary>
/// The hub connections held by THIS replica (connection id → caller context, user, session), indexed by user so a
/// revocation only looks at that user's sockets. Filled and cleared by <see cref="SessionRevocationHubFilter"/> on
/// connect/disconnect; used to close the sockets of revoked sessions. In memory by design: another replica's sockets
/// are reached by <see cref="IHubRevocationBroadcaster"/>, not by sharing this state.
/// </summary>
public sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, TrackedHubConnection>> _byUser = new();
    private readonly ConcurrentDictionary<string, Guid> _userOfConnection = new();

    public int Count => _userOfConnection.Count;

    public void Add(HubCallerContext context)
    {
        if (!Guid.TryParse(context.UserIdentifier, out var userId))
            return;

        _userOfConnection[context.ConnectionId] = userId;
        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, TrackedHubConnection>())[context.ConnectionId] =
            new TrackedHubConnection(context.ConnectionId, userId, SessionIdOf(context.User), context);
    }

    public void Remove(string connectionId)
    {
        if (!_userOfConnection.TryRemove(connectionId, out var userId))
            return;

        if (_byUser.TryGetValue(userId, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
                _byUser.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<string, TrackedHubConnection>>(userId, connections));
        }
    }

    /// <summary>The connections of <paramref name="userId"/> that belong to one of <paramref name="sessionIds"/>, or all when <paramref name="allSessions"/>.</summary>
    public IReadOnlyList<TrackedHubConnection> Find(Guid userId, IReadOnlyCollection<Guid>? sessionIds, bool allSessions) =>
        !_byUser.TryGetValue(userId, out var connections)
            ? []
            : connections.Values
                .Where(c => allSessions || (c.SessionId is { } sid && sessionIds is not null && sessionIds.Contains(sid)))
                .ToList();

    /// <summary>The <c>sid</c> claim (refresh-token family) of an access token; null for tokens without a session.</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal? user)
    {
        var claim = user?.FindFirst("sid")?.Value ?? user?.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public sealed record TrackedHubConnection(string ConnectionId, Guid UserId, Guid? SessionId, HubCallerContext Context);
