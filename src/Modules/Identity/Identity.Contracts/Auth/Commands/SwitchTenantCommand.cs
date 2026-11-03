namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// Multi-tenancy: issues a new session (access + refresh token) whose active tenant is <see cref="TenantId"/>.
/// The caller must be an active member. The previous session stays valid (one session per tenant/tab is fine).
/// </summary>
public record SwitchTenantCommand(Guid TenantId) : ICommand<SwitchTenantResult>;

public record SwitchTenantResult(string AccessToken, string RefreshToken, DateTime ExpiresAt, Guid TenantId);
