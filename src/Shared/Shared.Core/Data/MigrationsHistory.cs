using Humanizer;
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
}
