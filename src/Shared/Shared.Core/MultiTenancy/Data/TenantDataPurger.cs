using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Irreversible erasure of one tenant's rows (GDPR "right to erasure" / tenant offboarding). Works on any DbContext
/// without module knowledge: every table of an <see cref="IMustHaveTenant"/> entity is cleared with
/// <c>DELETE … WHERE tenant_id = @tenant</c>, dependents before principals (FK order from the EF model), after
/// nulling nullable self-references (e.g. category parents). Must run in HOST scope (RLS bypass).
/// </summary>
public static class TenantDataPurger
{
    /// <returns>Rows deleted.</returns>
    public static async Task<int> PurgeRowsAsync(DbContext context, Guid tenantId, CancellationToken ct = default)
    {
        var tables = TenantTablesInDeleteOrder(context.Model);
        var deleted = 0;

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            deleted = 0;
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            foreach (var table in tables)
            {
                foreach (var selfReference in table.NullableSelfReferenceColumns)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        $"UPDATE {table.Name} SET \"{selfReference}\" = NULL WHERE tenant_id = @tenant",
                        [new NpgsqlParameter("tenant", tenantId)], ct);
                }

                deleted += await context.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM {table.Name} WHERE tenant_id = @tenant",
                    [new NpgsqlParameter("tenant", tenantId)], ct);
            }

            await transaction.CommitAsync(ct);
        });

        return deleted;
    }

    /// <summary>Drops a dedicated tenant database (connections are terminated first).</summary>
    public static async Task DropDatabaseAsync(string tenantConnectionString, CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(tenantConnectionString);
        var database = builder.Database ?? throw new InvalidOperationException("The connection string has no database.");
        builder.Database = "postgres";
        builder.Pooling = false;

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{database.Replace("\"", "\"\"")}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync(ct);
    }

    public sealed record TenantTable(string Name, IReadOnlyList<string> NullableSelfReferenceColumns);

    public static IReadOnlyList<TenantTable> TenantTablesInDeleteOrder(IModel model)
    {
        var entities = model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.BaseType is null && e.ClrType.IsAssignableTo(typeof(IMustHaveTenant)) &&
                        e.GetTableName() is not null)
            .ToList();

        // Topological order: an entity is deleted only after every entity that references it.
        var ordered = new List<IEntityType>();
        var visited = new HashSet<IEntityType>();

        void Visit(IEntityType entity)
        {
            if (!visited.Add(entity)) return;
            foreach (var dependent in entities.Where(d => d != entity &&
                                                          d.GetForeignKeys().Any(fk => fk.PrincipalEntityType == entity)))
            {
                Visit(dependent);
            }

            ordered.Add(entity);
        }

        foreach (var entity in entities) Visit(entity);

        return ordered
            .Select(e => new TenantTable(
                $"\"{e.GetSchema() ?? "public"}\".\"{e.GetTableName()}\"",
                e.GetForeignKeys()
                    .Where(fk => fk.PrincipalEntityType == e && fk.Properties.All(p => p.IsNullable))
                    .SelectMany(fk => fk.Properties)
                    .Select(p => p.GetColumnName(StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema())) ?? p.Name)
                    .ToList()))
            .ToList();
    }
}
