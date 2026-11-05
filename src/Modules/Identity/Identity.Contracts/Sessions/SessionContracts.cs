using Identity.Contracts.Audit;

namespace Identity.Contracts.Sessions;

/// <summary>One signed-in device/browser (a refresh-token family). <see cref="Id"/> is the session id (<c>sid</c> claim).</summary>
public sealed record SessionDto(
    Guid Id,
    string? DeviceName,
    string? UserAgent,
    string IpAddress,
    DateTime SignedInAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    bool IsCurrent,
    Guid? TenantId);

/// <summary>The caller's active sessions; the one making this request is flagged <see cref="SessionDto.IsCurrent"/>.</summary>
public sealed record GetMySessionsQuery : IQuery<List<SessionDto>>;

/// <summary>Signs one of the caller's own devices out. The session's access token stops working immediately.</summary>
public sealed record RevokeMySessionCommand(Guid SessionId) : ICommand<RevokeSessionsResult>;

/// <summary>Signs every device out except the current one.</summary>
public sealed record RevokeOtherSessionsCommand : ICommand<RevokeSessionsResult>;

public sealed record RevokeSessionsResult(int Revoked);

/// <summary>Support: the active sessions of another user.</summary>
public sealed record GetUserSessionsQuery(Guid UserId) : IQuery<List<SessionDto>>;

/// <summary>Support: signs one session of another user out (audited).</summary>
public sealed record RevokeUserSessionCommand(Guid UserId, Guid SessionId)
    : ICommand<RevokeSessionsResult>, IAuditedCommand;
