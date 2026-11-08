using System.Text.RegularExpressions;
using Tenancy.Domains.FeatureFlags.Events;
using Tenancy.Domains.FeatureFlags.Exceptions;

namespace Tenancy.Domains.FeatureFlags.Models;

/// <summary>
/// A runtime feature flag (platform level, host database). Evaluated by
/// <c>Shared.Core.FeatureFlags.FeatureFlagEvaluator</c>; this aggregate only guards the data. Every change raises
/// <see cref="FeatureFlagChangedEvent"/> so the flag caches (every replica) are dropped after the commit.
/// </summary>
public partial class FeatureFlag : Aggregate<Guid>
{
    public const int KeyMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const string KeyPattern = "^[a-z0-9][a-z0-9._-]*$";

    private readonly List<FeatureFlagOverride> _overrides = [];

    public string Key { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>The flag's own value when no override applies and no rollout is set.</summary>
    public bool Enabled { get; private set; }

    /// <summary>0 = no rollout, 1..99 = share of tenants, 100 = everyone.</summary>
    public int RolloutPercent { get; private set; }

    /// <summary>Emergency off: wins over every override and rollout.</summary>
    public bool KillSwitch { get; private set; }

    public IReadOnlyCollection<FeatureFlagOverride> Overrides => _overrides.AsReadOnly();

    private FeatureFlag()
    {
    }

    public static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

    public static bool IsValidKey(string key) =>
        key.Length is > 0 and <= KeyMaxLength && KeyRegex().IsMatch(key);

    public static FeatureFlag Create(string key, string? description, bool enabled, int rolloutPercent, bool killSwitch)
    {
        var flag = new FeatureFlag { Id = Guid.CreateVersion7(), Key = ValidKey(key) };
        flag.Apply(description, enabled, rolloutPercent, killSwitch);
        return flag;
    }

    public void Update(string? description, bool enabled, int rolloutPercent, bool killSwitch) =>
        Apply(description, enabled, rolloutPercent, killSwitch);

    /// <summary>Adds or replaces the override of one tenant or one user (exactly one id).</summary>
    public FeatureFlagOverride SetOverride(Guid? tenantId, Guid? userId, bool enabled)
    {
        if ((tenantId is null) == (userId is null))
            throw new InvalidFeatureFlagException("An override targets exactly one of tenantId or userId.");

        var existing = _overrides.FirstOrDefault(o => o.TenantId == tenantId && o.UserId == userId);
        if (existing is not null)
        {
            existing.SetEnabled(enabled);
        }
        else
        {
            existing = FeatureFlagOverride.Create(Id, tenantId, userId, enabled);
            _overrides.Add(existing);
        }

        BumpVersionOnSave();
        Touch();
        return existing;
    }

    public void RemoveOverride(Guid overrideId)
    {
        var existing = _overrides.FirstOrDefault(o => o.Id == overrideId)
                       ?? throw new FeatureFlagOverrideNotFoundException(Key, overrideId);
        _overrides.Remove(existing);
        BumpVersionOnSave();
        Touch();
    }

    /// <summary>Raised by a delete so the caches drop the flag too.</summary>
    public void MarkDeleted() => Touch();

    private void Apply(string? description, bool enabled, int rolloutPercent, bool killSwitch)
    {
        if (rolloutPercent is < 0 or > 100)
            throw new InvalidFeatureFlagException("RolloutPercent must be between 0 and 100.");
        if (description is { Length: > DescriptionMaxLength })
            throw new InvalidFeatureFlagException($"Description is limited to {DescriptionMaxLength} characters.");

        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Enabled = enabled;
        RolloutPercent = rolloutPercent;
        KillSwitch = killSwitch;
        Touch();
    }

    private void Touch() => AddDomainEvent(new FeatureFlagChangedEvent(Key));

    private static string ValidKey(string key)
    {
        var normalized = NormalizeKey(key);
        if (!IsValidKey(normalized))
            throw new InvalidFeatureFlagException(
                $"A flag key is 1-{KeyMaxLength} characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.");
        return normalized;
    }

    [GeneratedRegex(KeyPattern)]
    private static partial Regex KeyRegex();
}

/// <summary>
/// Value of the flag for one tenant (<see cref="TenantId"/>) or one user (<see cref="UserId"/>). The target is NOT an
/// owner: the row belongs to the flag, so it is deliberately not <c>IMustHaveTenant</c>.
/// </summary>
public class FeatureFlagOverride : Entity<Guid>
{
    public Guid FeatureFlagId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool Enabled { get; private set; }

    private FeatureFlagOverride()
    {
    }

    internal static FeatureFlagOverride Create(Guid flagId, Guid? tenantId, Guid? userId, bool enabled) => new()
    {
        Id = Guid.CreateVersion7(),
        FeatureFlagId = flagId,
        TenantId = tenantId,
        UserId = userId,
        Enabled = enabled
    };

    internal void SetEnabled(bool enabled) => Enabled = enabled;
}
