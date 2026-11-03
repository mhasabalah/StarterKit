using Shared.Contracts.Events;

namespace Identity.Domains.Users.Events;

/// <summary>
/// Event raised when user status is updated
/// </summary>
public record UserStatusUpdatedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public bool IsActive { get; init; }
    public string? Reason { get; init; }
    public Guid UpdatedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
