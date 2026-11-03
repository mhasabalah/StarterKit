using System.Data.Common;
using Microsoft.Extensions.Options;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Postgres Row-Level Security session variables for <see cref="ITenantDataContext"/> connections. On every
/// connection open it sets:
/// <list type="bullet">
/// <item><c>app.tenant_id</c> = current tenant id (empty when unresolved → RLS returns nothing: fail closed).</item>
/// <item><c>app.bypass_rls</c> = <c>on</c> in host scope or when RLS is disabled in options, else <c>off</c>.</item>
/// </list>
/// Values are session-level and re-set on every open, so a pooled physical connection can never carry a previous
/// tenant into the next logical connection. Policies are installed by
/// <see cref="TenantRlsMigrationExtensions.EnableTenantRowLevelSecurity"/>.
/// </summary>
public sealed class TenantSessionConnectionInterceptor(
    ITenantContext tenantContext,
    IOptions<MultiTenancyOptions> options) : DbConnectionInterceptor
{
    public const string TenantSetting = "app.tenant_id";
    public const string BypassSetting = "app.bypass_rls";

    private const string Sql =
        "SELECT set_config('" + TenantSetting + "', @tenant, false), set_config('" + BypassSetting + "', @bypass, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var bypass = tenantContext.IsHostScope || !options.Value.Database.EnableRowLevelSecurity;
        var tenant = tenantContext.TenantId?.ToString() ?? string.Empty;

        var command = connection.CreateCommand();
        command.CommandText = Sql;
        AddParameter(command, "tenant", tenant);
        AddParameter(command, "bypass", bypass ? "on" : "off");
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
