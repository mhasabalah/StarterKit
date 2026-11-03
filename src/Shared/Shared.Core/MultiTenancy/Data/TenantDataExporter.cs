using System.IO.Compression;
using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Data portability (GDPR art. 20 / customer offboarding): writes every tenant table of a tenant-data context as
/// CSV into a zip, without module knowledge (same table discovery as <see cref="TenantDataPurger"/>).
/// <para>
/// Runs in the CALLER'S tenant scope (not host scope): the connection carries <c>app.tenant_id</c>, so RLS limits the
/// rows to the tenant in addition to the explicit <c>WHERE tenant_id = …</c>. Rows are streamed with Postgres
/// <c>COPY … TO STDOUT</c> - nothing is materialized in memory.
/// </para>
/// </summary>
public static class TenantDataExporter
{
    /// <returns>Number of tables written.</returns>
    public static async Task<int> WriteCsvAsync(DbContext context, Guid tenantId, ZipArchive zip, string folder,
        CancellationToken ct = default)
    {
        var tables = TenantDataPurger.TenantTablesInDeleteOrder(context.Model);
        if (tables.Count == 0) return 0;

        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            foreach (var table in tables.Reverse())
            {
                // tenantId is a Guid (no injection surface); COPY does not accept parameters.
                var sql = $"COPY (SELECT * FROM {table.Name} WHERE tenant_id = '{tenantId:D}') " +
                          "TO STDOUT WITH (FORMAT csv, HEADER true)";

                var entry = zip.CreateEntry($"{folder}/{FileNameOf(table.Name)}.csv", CompressionLevel.Optimal);
                await using var output = entry.Open();
                await using var writer = new StreamWriter(output, new System.Text.UTF8Encoding(false));
                using var reader = await connection.BeginTextExportAsync(sql, ct);

                var buffer = new char[16 * 1024];
                int read;
                while ((read = await reader.ReadAsync(buffer, ct)) > 0)
                {
                    await writer.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return tables.Count;
    }

    /// <summary><c>"catalog"."products"</c> → <c>catalog.products</c>.</summary>
    internal static string FileNameOf(string quotedTable) => quotedTable.Replace("\"", string.Empty);
}
