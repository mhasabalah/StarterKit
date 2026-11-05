using Identity.Domains.Sso.Events;
using Identity.Services.Sso;

namespace Identity.Domains.Sso.EventHandlers;

/// <summary>
/// A connection changed (authority, keys, status, deleted): drop the cached OIDC discovery documents of its current
/// and previous authority on EVERY replica (local eviction + Redis broadcast), so the next sign-in re-reads them.
/// Best effort - the cache also refreshes on its own interval and on unknown signing keys.
/// </summary>
public sealed class SsoConnectionChangedEventHandler(OidcConfigurationCache oidcConfigurations)
    : INotificationHandler<SsoConnectionChangedEvent>
{
    public Task Handle(SsoConnectionChangedEvent notification, CancellationToken cancellationToken) =>
        oidcConfigurations.ForgetAsync(
            [notification.OidcAuthority, notification.PreviousOidcAuthority], cancellationToken);
}
