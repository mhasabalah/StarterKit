using Shared.Core.DDD;

namespace Identity.Domains.Users.Events;

/// <summary>
/// Event raised when a user account is unlocked
/// </summary>
public record UserUnlockedEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public string? UnlockReason { get; init; }
    public Guid UnlockedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
