using Shared.Contracts.Events;

namespace Identity.Domains.Admins.Events;

/// <summary>
/// Event raised when a role is assigned to a user
/// </summary>
public record RoleAssignedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = default!;
    public string? Reason { get; init; }
    public Guid AssignedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
