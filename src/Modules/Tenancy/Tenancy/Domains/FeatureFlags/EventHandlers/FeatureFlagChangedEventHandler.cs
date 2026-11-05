using Tenancy.Domains.FeatureFlags.Events;
using Tenancy.Domains.FeatureFlags.Services;

namespace Tenancy.Domains.FeatureFlags.EventHandlers;

/// <summary>Drops the cached flag snapshot (this instance and, via the invalidation bus, every other) after a flag change commits.</summary>
public sealed class FeatureFlagChangedEventHandler(FeatureFlagStore store) : INotificationHandler<FeatureFlagChangedEvent>
{
    public Task Handle(FeatureFlagChangedEvent notification, CancellationToken cancellationToken) =>
        store.InvalidateAsync(cancellationToken);
}
