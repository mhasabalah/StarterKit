using System.Runtime.CompilerServices;

namespace Shared.Contracts.Events;

/// <summary>
/// Domain event. <see cref="EventId"/> and <see cref="OccurredOn"/> are assigned once per event instance
/// (first read) and are stable afterwards; <see cref="OccurredOn"/> is UTC.
/// Implementers may declare their own <c>OccurredOn { get; init; } = DateTime.UtcNow</c> to fix it at construction.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId => DomainEventMetadata.For(this).EventId;
    public DateTime OccurredOn => DomainEventMetadata.For(this).OccurredOn;
}

/// <summary>
/// Per-instance storage for the interface's default members (interfaces can't hold fields).
/// Keyed by reference identity, so value-equal records still get distinct ids; entries die with the event.
/// </summary>
internal static class DomainEventMetadata
{
    private static readonly ConditionalWeakTable<IDomainEvent, Entry> Entries = new();

    public static Entry For(IDomainEvent domainEvent) =>
        Entries.GetValue(domainEvent, static _ => new Entry(Guid.NewGuid(), DateTime.UtcNow));

    internal sealed record Entry(Guid EventId, DateTime OccurredOn);
}
