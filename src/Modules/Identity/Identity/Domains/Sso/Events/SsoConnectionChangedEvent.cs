namespace Identity.Domains.Sso.Events;

/// <summary>
/// Any configuration / status / domain change of a connection. The discovery and protocol-metadata caches listen to
/// it, so an edit takes effect on the next sign-in. It carries the OIDC authority (current and, after an authority
/// edit, the previous one) so the handler needs no reload - the row may already be deleted.
/// </summary>
public sealed record SsoConnectionChangedEvent(
    Guid ConnectionId,
    Guid TenantId,
    string? OidcAuthority = null,
    string? PreviousOidcAuthority = null) : IDomainEvent;
