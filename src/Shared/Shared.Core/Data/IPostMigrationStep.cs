namespace Shared.Core.Data;

/// <summary>
/// Schema work a module does right after the migrations of one of its contexts, when it depends on configuration and so
/// cannot be a migration (for example an index sized by a setting). It runs wherever migrations run (the
/// <c>--migrate-only</c> deploy step, boot-time migration in development and tests, every dedicated tenant database),
/// under the same advisory lock and on the same connection (the privileged <c>Database:MigrationConnectionString</c>
/// when one is set), and never at boot in production. Steps must be idempotent: they run on every migration pass.
/// </summary>
public interface IPostMigrationStep
{
    /// <summary>The context whose migrations this step follows.</summary>
    Type DbContextType { get; }

    /// <param name="migratedContext">The context that was just migrated, on the migration connection.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    Task RunAsync(DbContext migratedContext, CancellationToken cancellationToken);
}
