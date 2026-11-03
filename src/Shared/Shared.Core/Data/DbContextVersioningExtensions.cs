namespace Shared.Core.Data;

/// <summary>
/// Optimistic-concurrency helpers for command handlers.
/// </summary>
public static class DbContextVersioningExtensions
{
    /// <summary>
    /// Tells EF which version the client last saw. On SaveChanges EF emits
    /// <c>UPDATE ... WHERE version = <paramref name="expectedVersion"/></c>. If the row has moved on,
    /// a <see cref="DbUpdateConcurrencyException"/> is raised, which <c>ConcurrencyHandlingBehavior</c>
    /// turns into HTTP 409.
    /// <para>
    /// Call this after loading the entity (tracked) and before SaveChanges. A <c>null</c> version means the
    /// client did not send one: the loaded version is used, so the request is still protected against races
    /// between load and save.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var product = await repository.GetByIdAsync(command.Id, ct) ?? throw new ProductNotFoundException(command.Id);
    /// dbContext.ExpectVersion(product, command.Version);
    /// product.Update(...);
    /// await repository.SaveChangesAsync(ct);
    /// </code>
    /// </example>
    public static void ExpectVersion<TEntity>(this DbContext dbContext, TEntity entity, long? expectedVersion)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(entity);

        if (expectedVersion is null)
        {
            return;
        }

        var entry = dbContext.Entry(entity);
        if (entry.State is EntityState.Detached or EntityState.Added)
        {
            throw new InvalidOperationException(
                $"ExpectVersion requires a tracked, persisted {typeof(TEntity).Name}; current state is {entry.State}.");
        }

        entry.Property(e => e.Version).OriginalValue = expectedVersion.Value;
    }
}
