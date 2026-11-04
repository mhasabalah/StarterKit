using Microsoft.EntityFrameworkCore.Migrations;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Migration helpers for Postgres Row-Level Security on tenant tables (the database-level isolation layer under
/// the EF <c>Tenant</c> filter). Use in the migration that adds <c>tenant_id</c>:
/// <code>migrationBuilder.EnableTenantRowLevelSecurity("catalog", "products");</code>
/// <para>
/// The policy admits a row when the schema function <c>tenant_rls_bypass()</c> is true or its <c>tenant_id</c>
/// equals <c>app.tenant_id</c>; the session variables are set per connection by
/// <see cref="TenantSessionConnectionInterceptor"/>. <c>tenant_rls_bypass()</c> honors <c>app.bypass_rls = 'on'</c>
/// (host scope / RLS disabled) unless the hardened role mode is installed: when the group role
/// <see cref="BypassRoleName"/> exists in the cluster, only its members may bypass via the variable, so a runtime role
/// that is not a member cannot switch RLS off with <c>set_config</c> (host work then uses a <c>BYPASSRLS</c> role, see
/// <c>MultiTenancy:Database:RlsBypass</c>). Without that role the behavior is the session-variable mode.
/// <c>FORCE</c> makes it apply to the table owner too. Superusers always bypass RLS - production must connect as a
/// non-superuser role (see wiki/MultiTenancy/Isolation-and-Dedicated-Databases.md).
/// </para>
/// </summary>
public static class TenantRlsMigrationExtensions
{
    public const string PolicyName = "tenant_isolation";

    /// <summary>Optional group role; when it exists, the <c>app.bypass_rls</c> variable is honored only for its members.</summary>
    public const string BypassRoleName = "tenant_rls_bypass";

    private const string FunctionName = "tenant_rls_bypass";

    private static string Predicate(string schema) =>
        $"\"{schema}\".{FunctionName}() " +
        "OR tenant_id = NULLIF(current_setting('" + TenantSessionConnectionInterceptor.TenantSetting + "', true), '')::uuid";

    private static string BypassFunction(string schema) =>
        $"CREATE OR REPLACE FUNCTION \"{schema}\".{FunctionName}() RETURNS boolean LANGUAGE sql STABLE AS $fn$ " +
        "SELECT current_setting('" + TenantSessionConnectionInterceptor.BypassSetting + "', true) = 'on' " +
        $"AND (NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{BypassRoleName}') " +
        $"OR pg_catalog.pg_has_role(current_user, '{BypassRoleName}', 'member')) $fn$;";

    public static void EnableTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        var qualified = Qualify(schema, table);
        migrationBuilder.Sql(BypassFunction(schema));
        migrationBuilder.Sql($"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName} ON {qualified};");
        migrationBuilder.Sql(
            $"CREATE POLICY {PolicyName} ON {qualified} USING ({Predicate(schema)}) WITH CHECK ({Predicate(schema)});");
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
