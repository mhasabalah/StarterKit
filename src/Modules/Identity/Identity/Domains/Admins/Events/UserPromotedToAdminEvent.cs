using Shared.Contracts.Events;

namespace Identity.Domains.Admins.Events;

/// <summary>
/// Event raised when a user is promoted to admin
/// </summary>
public record UserPromotedToAdminEvent : IDomainEvent
{
    public Guid UserId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public string Reason { get; init; } = default!;
    public Guid PromotedBy { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
