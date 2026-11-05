namespace Tenancy.Contracts.FeatureFlags;

/// <summary>
/// A stored runtime feature flag. Evaluation order (see wiki/Architecture/Feature-Flags.md): kill switch, user
/// override, tenant override, percentage rollout, <see cref="Enabled"/>.
/// <see cref="RolloutPercent"/>: 0 = off (use <see cref="Enabled"/>), 1..99 = that share of tenants (stable hash), 100 = all.
/// </summary>
public sealed record FeatureFlagDto(
    Guid Id,
    string Key,
    string? Description,
    bool Enabled,
    int RolloutPercent,
    bool KillSwitch,
    IReadOnlyList<FeatureFlagOverrideDto> Overrides,
    long Version);

/// <summary>Exactly one of <see cref="TenantId"/> / <see cref="UserId"/> is set.</summary>
public sealed record FeatureFlagOverrideDto(Guid Id, Guid? TenantId, Guid? UserId, bool Enabled);

/// <summary>Every stored flag with its overrides (platform admins).</summary>
public sealed record GetFeatureFlagsQuery : IQuery<IReadOnlyList<FeatureFlagDto>>;

public sealed record GetFeatureFlagByKeyQuery(string Key) : IQuery<FeatureFlagDto>;

/// <summary>Every flag evaluated for the caller (current tenant and user); what clients use to toggle UI.</summary>
public sealed record GetMyFeatureFlagsQuery : IQuery<IReadOnlyDictionary<string, bool>>;

public sealed record CreateFeatureFlagCommand(
    string Key,
    string? Description,
    bool Enabled = false,
    int RolloutPercent = 0,
    bool KillSwitch = false) : ICommand<CreateFeatureFlagResult>;

public sealed record CreateFeatureFlagResult(Guid Id, string Key);

public sealed record UpdateFeatureFlagCommand(
    string Key,
    string? Description,
    bool Enabled,
    int RolloutPercent,
    bool KillSwitch,
    long? Version = null) : ICommand<UpdateFeatureFlagResult>;

public sealed record UpdateFeatureFlagResult(Guid Id, long Version);

public sealed record DeleteFeatureFlagCommand(string Key) : ICommand<DeleteFeatureFlagResult>;

public sealed record DeleteFeatureFlagResult(string Key);

/// <summary>Adds or replaces the override of one tenant OR one user (exactly one id).</summary>
public sealed record SetFeatureFlagOverrideCommand(string Key, Guid? TenantId, Guid? UserId, bool Enabled)
    : ICommand<SetFeatureFlagOverrideResult>;

public sealed record SetFeatureFlagOverrideResult(Guid OverrideId);

public sealed record RemoveFeatureFlagOverrideCommand(string Key, Guid OverrideId)
    : ICommand<RemoveFeatureFlagOverrideResult>;

public sealed record RemoveFeatureFlagOverrideResult(Guid OverrideId);
