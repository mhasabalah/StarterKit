using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Copies ONE tenant's rows of a tenant-data context from a source database to a target database (tenant move
/// shared ⇄ dedicated), generically - no module knowledge, same table discovery as <see cref="TenantDataPurger"/>.
/// <list type="bullet">
/// <item><b>Consistent</b>: the source is read in one <c>REPEATABLE READ READ ONLY</c> snapshot.</item>
/// <item><b>Atomic</b>: the target is written in ONE transaction - first the tenant's leftover rows of a previous
/// failed attempt are deleted (idempotent retries), then each table is streamed with binary <c>COPY</c>
/// (principals before dependents; self-references work because FK checks of a COPY run at end of statement).</item>
/// <item><b>Verified</b>: per-table row counts of target and source snapshot must match before the commit.</item>
/// <item><b>Schema-safe</b>: both databases must have the same columns (migrate the target first); generated
/// columns are skipped.</item>
/// </list>
/// Both connections run with <c>app.bypass_rls = on</c> (host operation). The caller guarantees the tenant receives
/// no writes meanwhile (status <c>Migrating</c>).
/// </summary>
public static class TenantDataMover
{
    /// <returns>Rows copied per table (<c>schema.table</c>).</returns>
    public static async Task<IReadOnlyDictionary<string, long>> CopyAsync(DbContext source, DbContext target,
        Guid tenantId, CancellationToken ct = default)
    {
        var tables = TenantDataPurger.TenantTablesInDeleteOrder(source.Model);
        var copied = new Dictionary<string, long>(StringComparer.Ordinal);
        if (tables.Count == 0) return copied;

        var sourceConnection = await OpenAsync(source, ct);
        var targetConnection = await OpenAsync(target, ct);
        if (SameDatabase(sourceConnection, targetConnection))
        {
            throw new InvalidOperationException("The source and target of a tenant move are the same database.");
        }

        await using var snapshot = await sourceConnection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await ExecuteAsync(sourceConnection, snapshot, "SET TRANSACTION READ ONLY", ct);
        await using var transaction = await targetConnection.BeginTransactionAsync(ct);

        // 1. Leftovers of a previous failed attempt (dependents first).
        foreach (var table in tables)
        {
            foreach (var column in table.NullableSelfReferenceColumns)
            {
                await ExecuteAsync(targetConnection, transaction,
                    $"UPDATE {table.Name} SET \"{column}\" = NULL WHERE tenant_id = '{tenantId:D}'", ct);
            }

            await ExecuteAsync(targetConnection, transaction, $"DELETE FROM {table.Name} WHERE tenant_id = '{tenantId:D}'", ct);
        }

        // 2. Copy (principals first) + verify.
        foreach (var table in tables.Reverse())
        {
            var columns = await ColumnsAsync(sourceConnection, snapshot, table.Name, ct);
            var targetColumns = await ColumnsAsync(targetConnection, transaction, table.Name, ct);
            if (!columns.ToHashSet(StringComparer.Ordinal).SetEquals(targetColumns))
            {
                throw new InvalidOperationException(
                    $"Schema of {table.Name} differs between source and target; migrate both to the same version first.");
            }

            var columnList = string.Join(", ", columns.Select(c => $"\"{c}\""));
            var filter = $"tenant_id = '{tenantId:D}'";

            var reader = await sourceConnection.BeginRawBinaryCopyAsync(
                $"COPY (SELECT {columnList} FROM {table.Name} WHERE {filter}) TO STDOUT (FORMAT BINARY)", ct);
            var writer = await targetConnection.BeginRawBinaryCopyAsync(
                $"COPY {table.Name} ({columnList}) FROM STDIN (FORMAT BINARY)", ct);
            try
            {
                await reader.CopyToAsync(writer, ct);
            }
            catch
            {
                // Abort both COPYs; the target transaction is rolled back by the caller's exception path.
                try { await writer.CancelAsync(); } catch { /* connection already broken */ }
                try { await reader.CancelAsync(); } catch { /* connection already broken */ }
                throw;
            }
            finally
            {
                await writer.DisposeAsync();
                await reader.DisposeAsync();
            }

            var expected = await CountAsync(sourceConnection, snapshot, table.Name, filter, ct);
            var actual = await CountAsync(targetConnection, transaction, table.Name, filter, ct);
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    $"Copy verification failed for {table.Name}: {expected} source row(s), {actual} target row(s).");
            }

            copied[table.Name.Replace("\"", string.Empty)] = actual;
        }

        await transaction.CommitAsync(ct);
        await snapshot.CommitAsync(ct);
        return copied;
    }

    /// <summary>Opens the context's connection (interceptors run) and switches it to host mode (RLS bypass).</summary>
    private static async Task<NpgsqlConnection> OpenAsync(DbContext context, CancellationToken ct)
    {
        await context.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await ExecuteAsync(connection, null, "SELECT set_config('app.bypass_rls', 'on', false)", ct);
        return connection;
    }

    private static bool SameDatabase(NpgsqlConnection a, NpgsqlConnection b) =>
        SameDatabase(a.ConnectionString, b.ConnectionString);

    /// <summary>Same server (host + port) and database name - i.e. a move would read and write the same rows.</summary>
    public static bool SameDatabase(string a, string b)
    {
        var x = new NpgsqlConnectionStringBuilder(a);
        var y = new NpgsqlConnectionStringBuilder(b);
        return string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase) && x.Port == y.Port &&
               string.Equals(x.Database, y.Database, StringComparison.Ordinal);
    }

    private static async Task<List<string>> ColumnsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string quotedTable, CancellationToken ct)
    {
        var parts = quotedTable.Split('.');
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = @schema AND table_name = @table AND is_generated = 'NEVER' ORDER BY ordinal_position",
            connection, transaction);
        command.Parameters.AddWithValue("schema", parts[0].Trim('"'));
        command.Parameters.AddWithValue("table", parts[1].Trim('"'));

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(0));
        if (columns.Count == 0) throw new InvalidOperationException($"Table {quotedTable} does not exist in a database of the move.");
        return columns;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table,
        string filter, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {filter}", connection, transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }
}
