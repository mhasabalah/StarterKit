using Microsoft.EntityFrameworkCore.Migrations;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Migration helpers for Postgres Row-Level Security on tenant tables (the database-level isolation layer under
/// the EF <c>Tenant</c> filter). Use in the migration that adds <c>tenant_id</c>:
/// <code>migrationBuilder.EnableTenantRowLevelSecurity("catalog", "products");</code>
/// <para>
/// The policy admits a row when <c>app.bypass_rls = 'on'</c> (host scope / RLS disabled) or its <c>tenant_id</c>
/// equals <c>app.tenant_id</c>; both are set per connection by <see cref="TenantSessionConnectionInterceptor"/>.
/// <c>FORCE</c> makes it apply to the table owner too. Superusers always bypass RLS - production must connect as a
/// non-superuser role (see wiki/MultiTenancy/Isolation-and-Dedicated-Databases.md).
/// </para>
/// </summary>
public static class TenantRlsMigrationExtensions
{
    public const string PolicyName = "tenant_isolation";

    private const string Predicate =
        "current_setting('" + TenantSessionConnectionInterceptor.BypassSetting + "', true) = 'on' " +
        "OR tenant_id = NULLIF(current_setting('" + TenantSessionConnectionInterceptor.TenantSetting + "', true), '')::uuid";

    public static void EnableTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        var qualified = Qualify(schema, table);
        migrationBuilder.Sql($"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName} ON {qualified};");
        migrationBuilder.Sql(
            $"CREATE POLICY {PolicyName} ON {qualified} USING ({Predicate}) WITH CHECK ({Predicate});");
    }

    public static void DisableTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        var qualified = Qualify(schema, table);
        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName} ON {qualified};");
        migrationBuilder.Sql($"ALTER TABLE {qualified} NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualified} DISABLE ROW LEVEL SECURITY;");
    }

    private static string Qualify(string schema, string table) => $"\"{schema}\".\"{table}\"";
}
