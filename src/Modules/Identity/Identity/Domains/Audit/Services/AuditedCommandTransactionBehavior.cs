using Identity.Contracts.Audit;

namespace Identity.Domains.Audit.Services;

/// <summary>
/// One database transaction on <see cref="ApplicationIdentityDbContext"/>: every UserManager/RoleManager save made
/// by <c>work</c> plus the audit rows recorded via <see cref="IIdentityAuditWriter"/> commit together, or nothing
/// commits. Nested calls join the outer transaction.
/// <para>
/// Do NOT put security counters (failed 2FA/password attempts) inside: a rejection throws, the transaction rolls
/// back and the strike would be lost. Check credentials first, then run the change in the unit of work.
/// </para>
/// </summary>
public interface IIdentityUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}

public sealed class IdentityUnitOfWork(ApplicationIdentityDbContext db) : IIdentityUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            // Nested: the outer unit of work owns the transaction and the final save.
            return await work();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work();

            // Flush audit rows (and anything else still pending) inside the transaction, then commit both.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // Rolled back (dispose without commit). Drop the tracked leftovers (e.g. the audit row) so a later save
            // in the same scope cannot persist half of the failed unit of work.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}

/// <summary>
/// Runs every <see cref="IAuditedCommand"/> inside <see cref="IIdentityUnitOfWork"/>, so admin mutations and their
/// audit entries are atomic without each handler opening a transaction. Other requests pass straight through (the
/// unit of work is resolved lazily so unrelated modules never pay for an Identity DbContext).
/// <para>
/// Registered after Shared.Core's behaviors, so it sits inside DispatchDomainEventsBehavior: domain events are
/// published only after the transaction committed.
/// </para>
/// </summary>
public sealed class AuditedCommandTransactionBehavior<TRequest, TResponse>(IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuditedCommand)
        {
            return next(cancellationToken);
        }

        return services.GetRequiredService<IIdentityUnitOfWork>()
            .ExecuteAsync(() => next(), cancellationToken);
    }
}
