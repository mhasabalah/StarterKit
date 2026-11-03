using Shared.Contracts.Events;

namespace Identity.Domains.Users.Events;

/// <summary>
/// Event raised when a user is deleted (soft delete)
/// </summary>
public record UserDeletedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid DeletedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
