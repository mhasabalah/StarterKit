namespace Tenancy.Domains.FeatureFlags.Events;

/// <summary>A flag (or one of its overrides) changed or was deleted: drop the cached flag snapshot on every replica.</summary>
public sealed record FeatureFlagChangedEvent(string Key) : IDomainEvent;
