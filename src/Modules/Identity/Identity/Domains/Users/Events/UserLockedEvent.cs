using Shared.Core.DDD;

namespace Identity.Domains.Users.Events;

/// <summary>
/// Event raised when a user account is locked
/// </summary>
public record UserLockedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = default!;
    public DateTimeOffset? LockoutEnd { get; init; }
    public Guid LockedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
