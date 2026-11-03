using Identity.Contracts.Audit;

namespace Identity.Contracts.Impersonation;

/// <summary>
/// Platform support (permission <c>tenants.impersonate</c>): a short-lived ACCESS token (no refresh token) acting as
/// <see cref="UserId"/> in <see cref="TenantId"/>. The token carries <c>act</c> = the platform user; it cannot change
/// credentials/account settings, mint sessions or perform owner actions, and every action is audited with the
/// impersonator. Only users WITHOUT platform roles (besides User) can be impersonated.
/// On success the handler publishes <c>UserDirectory.Contracts.Events.ImpersonationStartedEvent</c>.
/// </summary>
/// <param name="TenantId">Tenant to act in (single-tenant mode: the Default tenant).</param>
/// <param name="UserId">Member to act as.</param>
/// <param name="Reason">Mandatory justification (ticket reference…), stored in both audit trails.</param>
/// <param name="Minutes">Lifetime (default and maximum: <c>MultiTenancy:Impersonation:MaxMinutes</c>).</param>
public sealed record StartImpersonationCommand(Guid TenantId, Guid UserId, string Reason, int? Minutes = null)
    : ICommand<StartImpersonationResult>, IAuditedCommand;

public sealed record StartImpersonationResult(string AccessToken, DateTime ExpiresAt, Guid TenantId, Guid UserId);
