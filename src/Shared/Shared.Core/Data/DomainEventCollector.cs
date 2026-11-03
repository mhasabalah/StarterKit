namespace Shared.Core.Data;

/// <summary>
/// Scoped per-request (or per-job-unit-of-work) bag that <see cref="Interceptors.DispatchDomainEventsInterceptor"/>
/// writes into and <see cref="IDomainEventDispatcher"/> drains.
/// <para>
/// Events move through three stages so that ONLY events of committed work are ever dispatched:
/// </para>
/// <list type="number">
/// <item><b>Staged</b> (<see cref="Stage"/>): taken off the aggregates in <c>SavingChanges</c>, before the database
/// is touched. Discarded by <see cref="SaveFailed"/> (save threw, was cancelled, or hit a concurrency conflict).</item>
/// <item><b>Awaiting commit</b>: the save succeeded inside an explicit transaction
/// (<c>Database.BeginTransaction</c>). Promoted by <see cref="TransactionCommitted"/>, discarded by
/// <see cref="TransactionRolledBack"/>. A transaction that is disposed without commit never promotes them.</item>
/// <item><b>Ready</b>: committed. Returned by <see cref="DequeueAll"/>.</item>
/// </list>
/// The depth counter lets <c>DispatchDomainEventsBehavior</c> / <see cref="IUnitOfWorkRunner"/> know whether they
/// are the outermost unit of work (root). Only the root dispatches, so every event produced by the whole command
/// tree (nested <c>ISender.Send</c> calls included) is published exactly once, after the root finishes.
/// </summary>
public interface IDomainEventCollector
{
    /// <summary>Stages events taken off the aggregates of <paramref name="saveOperation"/> (the DbContext) before it saves.</summary>
    void Stage(object saveOperation, IReadOnlyCollection<INotification> events);

    /// <summary>
    /// The save of <paramref name="saveOperation"/> succeeded. With no <paramref name="openTransaction"/> its events
    /// are committed (ready); otherwise they wait for that transaction to commit.
    /// </summary>
    void SaveSucceeded(object saveOperation, object? openTransaction);

    /// <summary>The save of <paramref name="saveOperation"/> failed: its staged events are discarded.</summary>
    void SaveFailed(object saveOperation);

    /// <summary>Promotes every event saved inside <paramref name="transaction"/> to ready.</summary>
    void TransactionCommitted(object transaction);

    /// <summary>Discards every event saved inside <paramref name="transaction"/>.</summary>
    void TransactionRolledBack(object transaction);

    /// <summary>Adds already-committed events (ready to dispatch).</summary>
    void Collect(IEnumerable<INotification> events);

    /// <summary>Returns and removes every ready (committed) event.</summary>
    IReadOnlyList<INotification> DequeueAll();

    /// <summary>Number of ready (committed, not yet dispatched) events.</summary>
    int ReadyCount { get; }

    /// <summary>Discards everything: staged, awaiting-commit and ready events. Used when a unit of work fails.</summary>
    void Clear();

    /// <summary>Returns the new depth after incrementing.</summary>
    int IncrementDepth();

    void DecrementDepth();
}

public sealed class DomainEventCollector : IDomainEventCollector
{
    private readonly Lock _gate = new();
    private readonly Dictionary<object, List<INotification>> _staged = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<INotification>> _awaitingCommit = new(ReferenceEqualityComparer.Instance);
    private readonly List<INotification> _ready = [];
    private int _depth;

    public void Stage(object saveOperation, IReadOnlyCollection<INotification> events)
    {
        ArgumentNullException.ThrowIfNull(saveOperation);
        if (events.Count == 0) return;

        lock (_gate)
        {
            // Append: a previous SavingChanges that never completed (another interceptor threw before the
            // save started) left its changes in the change tracker, so this save persists them too.
            Bucket(_staged, saveOperation).AddRange(events);
        }
    }

    public void SaveSucceeded(object saveOperation, object? openTransaction)
    {
        ArgumentNullException.ThrowIfNull(saveOperation);

        lock (_gate)
        {
            if (!_staged.Remove(saveOperation, out var events)) return;

            if (openTransaction is null)
                _ready.AddRange(events);
            else
                Bucket(_awaitingCommit, openTransaction).AddRange(events);
        }
    }

    public void SaveFailed(object saveOperation)
    {
        ArgumentNullException.ThrowIfNull(saveOperation);
        lock (_gate) _staged.Remove(saveOperation);
    }

    public void TransactionCommitted(object transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        lock (_gate)
        {
            if (_awaitingCommit.Remove(transaction, out var events))
                _ready.AddRange(events);
        }
    }

    public void TransactionRolledBack(object transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        lock (_gate) _awaitingCommit.Remove(transaction);
    }

    public void Collect(IEnumerable<INotification> events)
    {
        lock (_gate) _ready.AddRange(events);
    }

    public IReadOnlyList<INotification> DequeueAll()
    {
        lock (_gate)
        {
            var snapshot = _ready.ToList();
            _ready.Clear();
            return snapshot;
        }
    }

    public int ReadyCount
    {
        get
        {
            lock (_gate) return _ready.Count;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _staged.Clear();
            _awaitingCommit.Clear();
            _ready.Clear();
        }
    }

    public int IncrementDepth() => Interlocked.Increment(ref _depth);

    public void DecrementDepth() => Interlocked.Decrement(ref _depth);

    private static List<INotification> Bucket(Dictionary<object, List<INotification>> buckets, object key)
    {
        if (!buckets.TryGetValue(key, out var list))
        {
            list = [];
            buckets[key] = list;
        }

        return list;
    }
}
