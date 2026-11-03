using Shared.Contracts.Events;

namespace Identity.Domains.Admins.Events;

/// <summary>
/// Event raised when a role is revoked from a user
/// </summary>
public record RoleRevokedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = default!;
    public string? Reason { get; init; }
    public Guid RevokedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
