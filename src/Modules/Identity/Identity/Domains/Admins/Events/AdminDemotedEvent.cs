using Shared.Core.DDD;

namespace Identity.Domains.Admins.Events;

/// <summary>
/// Event raised when an admin is demoted to regular user
/// </summary>
public record AdminDemotedEvent : IDomainEvent
{
    public Guid AdminId { get; init; }
    public string Reason { get; init; } = default!;
    public Guid DemotedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
