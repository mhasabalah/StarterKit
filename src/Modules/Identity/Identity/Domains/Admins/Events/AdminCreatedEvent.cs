using Shared.Core.DDD;

namespace Identity.Domains.Admins.Events;

/// <summary>
/// Event raised when a new admin is created
/// </summary>
public record AdminCreatedEvent : IDomainEvent
{
    public Guid AdminId { get; init; }
    public string UserName { get; init; } = default!;
    public bool IsSuperAdmin { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
