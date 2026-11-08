using Humanizer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Shared.Core.Data;

/// <summary>
/// One migrations-history table per module DbContext. All modules share ONE database, so each context needs its
/// own history table. With a shared table, a module's migrations can't be listed, rolled back or squashed on their own.
/// <para>
/// MUST be used both at runtime (<see cref="Extentions.AddCustomDbContext{TContext}"/>, which auto-migrates on
/// startup) and in every <c>IDesignTimeDbContextFactory</c> (<c>dotnet ef</c>). If the two disagree, the CLI and
/// the app track applied migrations in different tables and re-apply or skip migrations.
/// </para>
/// Convention: <c>public.__{context-name-without-DbContext, snake_case}_migrations_history</c>,
/// e.g. <c>CatalogDbContext</c> → <c>public.__catalog_migrations_history</c>.
/// </summary>
public static class MigrationsHistory
{
    public const string Schema = "public";

    public static string TableName(Type dbContextType)
    {
        ArgumentNullException.ThrowIfNull(dbContextType);
        var name = dbContextType.Name;
        if (name.EndsWith("DbContext", StringComparison.Ordinal))
        {
            name = name[..^"DbContext".Length];
        }

        return $"__{name.Underscore()}_migrations_history";
    }

    public static string TableName<TContext>() where TContext : DbContext => TableName(typeof(TContext));

    /// <summary>Applies the convention to an Npgsql options builder.</summary>
    public static NpgsqlDbContextOptionsBuilder UseModuleMigrationsHistory<TContext>(
        this NpgsqlDbContextOptionsBuilder builder) where TContext : DbContext =>
        builder.MigrationsHistoryTable(TableName<TContext>(), Schema);

    /// <summary>
    /// Creates the context's history table when it is missing, before anything reads it. On a fresh database EF Core
    /// otherwise reads the table first and logs the "relation does not exist" failure at Error level (once per read)
    /// before recovering: harmless, but it made every first start look broken. Skipped while the database itself
    /// does not exist (a new dedicated tenant database): <c>MigrateAsync</c> creates it.
    /// </summary>
    public static async Task EnsureHistoryTableAsync(this DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        if (await database.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
        {
            await database.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);
        }
    }
}
