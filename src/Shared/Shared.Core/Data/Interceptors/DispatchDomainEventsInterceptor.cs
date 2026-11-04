using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Shared.Core.Data.Interceptors;

/// <summary>
/// Moves domain events from the EF ChangeTracker into the scoped <see cref="IDomainEventCollector"/> and tracks
/// whether the work that produced them was actually committed.
/// <list type="bullet">
/// <item><c>SavingChanges</c>: takes the events off the aggregates and STAGES them (the save may still fail).</item>
/// <item><c>SavedChanges</c>: outside an explicit transaction the events are committed (ready); inside one they
/// wait for that transaction.</item>
/// <item><c>SaveChangesFailed</c> / <c>SaveChangesCanceled</c> / <c>ThrowingConcurrencyException</c>: the staged
/// events are DISCARDED, so a failed save never leaks events into the next command in the same scope.</item>
/// <item><c>TransactionCommitted</c> promotes the transaction's events; <c>TransactionRolledBack</c> (or a failed
/// commit/rollback) discards them. Keyed by the ADO.NET <see cref="DbTransaction"/>, so contexts sharing one
/// transaction behave correctly.</item>
/// </list>
/// Dispatching is intentionally NOT done here: <see cref="IDomainEventDispatcher"/> publishes the ready events once
/// the outermost unit of work (root mediator command via <c>DispatchDomainEventsBehavior</c>, or a background job via
/// <see cref="IUnitOfWorkRunner"/>) has completed. This guarantees that handlers see fully committed data, that
/// several saves in one unit of work publish once, and that there is no re-entrant SaveChanges on the same context.
/// </summary>
public class DispatchDomainEventsInterceptor(IDomainEventCollector collector)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    // ----- SaveChanges -----

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stage(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stage(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Failed(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        Failed(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    public override InterceptionResult ThrowingConcurrencyException(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result)
    {
        Failed(eventData.Context);
        return base.ThrowingConcurrencyException(eventData, result);
    }

    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    // ----- Transactions (IDbTransactionInterceptor; members not implemented keep their default no-op) -----

    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        collector.TransactionCommitted(transaction);

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {
        collector.TransactionCommitted(transaction);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        collector.TransactionRolledBack(transaction);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {
        collector.TransactionRolledBack(transaction);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (EndsTransaction(eventData))
            collector.TransactionRolledBack(transaction);
    }

    Task IDbTransactionInterceptor.TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken)
    {
        if (EndsTransaction(eventData))
            collector.TransactionRolledBack(transaction);
        return Task.CompletedTask;
    }

    // ----- helpers -----

    /// <summary>A failed commit leaves the outcome unknown: never publish events for possibly-uncommitted data.</summary>
    private static bool EndsTransaction(TransactionErrorEventData eventData) =>
        eventData.Action is "Commit" or "Rollback";

    private void Stage(DbContext? context)
    {
        if (context is null)
            return;

        var aggregates = context.ChangeTracker
            .Entries<IAggregate>()
            .Where(a => a.Entity.DomainEvents.Count > 0)
            .Select(a => a.Entity)
            .ToList();

        if (aggregates.Count == 0)
            return;

        var events = aggregates
            .SelectMany(a => a.ClearDomainEvents())
            .Cast<INotification>()
            .ToList();

        collector.Stage(context, events);
    }

    private void Saved(DbContext? context)
    {
        if (context is null)
            return;
        collector.SaveSucceeded(context, OpenTransactionKey(context));
    }

    private void Failed(DbContext? context)
    {
        if (context is null)
            return;
        collector.SaveFailed(context);
    }

    /// <summary>
    /// The explicit transaction the save ran in, or null. EF's own per-SaveChanges transaction is already committed
    /// and cleared by the time <c>SavedChanges</c> runs, so null means "committed".
    /// </summary>
    private static object? OpenTransactionKey(DbContext context)
    {
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null)
            return null;

        return transaction is IInfrastructure<DbTransaction> relational ? relational.Instance : transaction;
    }
}
