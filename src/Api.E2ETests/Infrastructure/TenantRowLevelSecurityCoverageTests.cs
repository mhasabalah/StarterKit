using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Xunit;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// RLS coverage guard over the REAL migrations (the host applied them at startup): in the schema of every
/// tenant-data context (<see cref="ITenantDataContext"/>), each table with a <c>tenant_id</c> column must have row-level
/// security ENABLED and FORCED and the <see cref="TenantRlsMigrationExtensions.PolicyName"/> policy.
/// <para>
/// Catches the easy-to-miss step of a new table/module: forgetting <c>migrationBuilder.EnableTenantRowLevelSecurity</c>
/// (the EF Tenant filter would still hide the gap in every other test). Schemas come from the registered contexts,
/// so a new tenant-data module is covered without editing this test; control-plane contexts (Tenancy, Identity...)
/// are deliberately out of scope.
/// </para>
/// </summary>
[Collection(E2ECollection.Name)]
public class TenantRowLevelSecurityCoverageTests(E2ETestFixture fixture)
{
    private const string CoverageSql = """
        SELECT n.nspname, c.relname, c.relrowsecurity, c.relforcerowsecurity,
               EXISTS (SELECT 1 FROM pg_policies p
                       WHERE p.schemaname = n.nspname AND p.tablename = c.relname AND p.policyname = @policy)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname = ANY(@schemas)
          AND EXISTS (SELECT 1 FROM information_schema.columns col
                      WHERE col.table_schema = n.nspname AND col.table_name = c.relname AND col.column_name = 'tenant_id')
        ORDER BY 1, 2
        """;

    [Fact]
    public async Task Every_tenant_table_of_every_tenant_data_module_has_forced_row_level_security()
    {
        await fixture.WithScopeAsync(async sp =>
        {
            using var host = TenantScope.BeginHostScope();
            var tenantContexts = sp.GetServices<IDbContext>().OfType<DbContext>().Where(c => c is ITenantDataContext).ToList();
            var schemas = tenantContexts.Select(c => c.Model.GetDefaultSchema()).OfType<string>().Distinct().ToArray();

            schemas.Should().Contain(["catalog", "attachment"], "the scan must find the tenant-data modules");

            var tables = await QueryAsync(tenantContexts[0], schemas);

            tables.Should().NotBeEmpty();
            foreach (var schema in schemas)
            {
                tables.Should().Contain(t => t.Schema == schema, $"schema '{schema}' must have tenant tables to check");
            }

            var violations = tables
                .Where(t => !t.Enabled || !t.Forced || !t.HasPolicy)
                .Select(t => $"{t.Schema}.{t.Table}: enabled={t.Enabled} forced={t.Forced} policy={t.HasPolicy}")
                .ToList();

            string.Join(Environment.NewLine, violations).Should().BeEmpty(
                "every tenant table needs migrationBuilder.EnableTenantRowLevelSecurity(\"<schema>\", \"<table>\") " +
                "in the migration that creates it (see /starterkit:add-migration)");
        });
    }

    private static async Task<List<(string Schema, string Table, bool Enabled, bool Forced, bool HasPolicy)>> QueryAsync(
        DbContext context, string[] schemas)
    {
        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
            await context.Database.OpenConnectionAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = CoverageSql;
            AddParameter(command, "policy", TenantRlsMigrationExtensions.PolicyName);
            AddParameter(command, "schemas", schemas);

            var result = new List<(string, string, bool, bool, bool)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3),
                    reader.GetBoolean(4)));
            }

            return result;
        }
        finally
        {
            if (opened)
                await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
