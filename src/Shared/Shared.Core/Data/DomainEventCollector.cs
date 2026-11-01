namespace Shared.Core.Data;

/// <summary>
/// Scoped per-request bag that the EF interceptor writes into and the
/// MediatR pipeline behavior reads from.
///
/// The depth counter lets the behavior know whether it is the outermost
/// command in a call chain (root) or a nested inner command invoked via
/// ISender.Send from inside a handler.  Only the root command dispatches
/// events — so all domain events produced by every SaveChanges call in
/// the entire command tree are batched and published exactly once, after
/// the outermost handler finishes.
/// </summary>
public interface IDomainEventCollector
{
    void Collect(IEnumerable<INotification> events);
    IReadOnlyList<INotification> DequeueAll();
    /// <summary>Returns the new depth after incrementing.</summary>
    int IncrementDepth();
    void DecrementDepth();
}

public sealed class DomainEventCollector : IDomainEventCollector
{
    private readonly List<INotification> _events = [];
    private int _depth;

    public void Collect(IEnumerable<INotification> events) => _events.AddRange(events);

    public IReadOnlyList<INotification> DequeueAll()
    {
        var snapshot = _events.ToList();
        _events.Clear();
        return snapshot;
    }

    public int IncrementDepth() => ++_depth;
    public void DecrementDepth() => --_depth;
}
