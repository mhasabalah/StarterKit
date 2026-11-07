namespace Identity.Domains.Sso.Events;

/// <summary>
/// An SAML IdP signing certificate is about to expire (raised once per expiry date and alert threshold). The handler
/// tells the tenant's SSO administrators.
/// </summary>
public sealed record SsoCertificateExpiringEvent(
    Guid ConnectionId,
    Guid TenantId,
    string ConnectionName,
    DateTime NotAfter,
    int DaysLeft) : IDomainEvent;
