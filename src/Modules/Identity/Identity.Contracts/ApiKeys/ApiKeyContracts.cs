using Identity.Contracts.Audit;

namespace Identity.Contracts.ApiKeys;

/// <summary>An API key as listed: never contains the secret, only the public <see cref="Prefix"/>.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    List<string> Scopes,
    Guid? TenantId,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    bool IsActive,
    Guid? UserId = null);

/// <summary>
/// Creates a key for the caller (and their active tenant). Effective permissions of the key are ALWAYS
/// the caller's role-derived permissions intersected with <see cref="Scopes"/>; a scope the caller does not hold is
/// simply ineffective.
/// </summary>
/// <param name="Scopes">Permission names (e.g. <c>catalog.moderate</c>, tenant permissions). At least one.</param>
/// <param name="ExpiresInDays">Lifetime; null = <c>Identity:ApiKeys:DefaultExpiryDays</c>. Capped by <c>MaxExpiryDays</c>.</param>
public sealed record CreateApiKeyCommand(string Name, List<string> Scopes, int? ExpiresInDays = null)
    : ICommand<CreateApiKeyResult>;

/// <summary>The only time the secret <see cref="ApiKey"/> (<c>sk_&lt;prefix&gt;_&lt;secret&gt;</c>) is ever returned.</summary>
public sealed record CreateApiKeyResult(ApiKeyDto Key, string ApiKey);

public sealed record GetMyApiKeysQuery : IQuery<List<ApiKeyDto>>;

public sealed record RevokeMyApiKeyCommand(Guid KeyId) : ICommand<RevokeApiKeyResult>;

public sealed record RevokeApiKeyResult(bool Revoked);

/// <summary>Admin: the keys of a user.</summary>
public sealed record GetUserApiKeysQuery(Guid UserId) : IQuery<List<ApiKeyDto>>;

/// <summary>Admin: revokes any key (audited).</summary>
public sealed record RevokeUserApiKeyCommand(Guid UserId, Guid KeyId)
    : ICommand<RevokeApiKeyResult>, IAuditedCommand;
