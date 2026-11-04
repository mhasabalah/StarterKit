using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Postgres Row-Level Security session variables for <see cref="ITenantDataContext"/> connections. On every
/// connection open it sets:
/// <list type="bullet">
/// <item><c>app.tenant_id</c> = current tenant id (empty when unresolved → RLS returns nothing: fail closed).</item>
/// <item><c>app.bypass_rls</c> = <c>on</c> in host scope or when RLS is disabled in options, else <c>off</c>.</item>
/// </list>
/// With <see cref="RlsBypassMode.Role"/> the variable is never set to <c>on</c>; host scope instead swaps the
/// credentials to the <c>BYPASSRLS</c> host role just before the connection opens.
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

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result)
    {
        UseHostRoleIfNeeded(connection);
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        UseHostRoleIfNeeded(connection);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    // Original runtime credentials per physical NpgsqlConnection object, captured once before any swap. EF reuses the
    // same connection object across opens, so credentials must be set deterministically on EVERY open.
    private static readonly ConditionalWeakTable<DbConnection, RuntimeCredentials> Original = new();

    private sealed record RuntimeCredentials(string? Username, string? Password);

    private void UseHostRoleIfNeeded(DbConnection connection)
    {
        var db = options.Value.Database;
        if (db.RlsBypass != RlsBypassMode.Role || !db.EnableRowLevelSecurity ||
            connection is not NpgsqlConnection npgsql || string.IsNullOrWhiteSpace(db.HostConnectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(npgsql.ConnectionString);
        var original = Original.GetValue(connection, _ => new RuntimeCredentials(builder.Username, builder.Password));

        if (tenantContext.IsHostScope)
        {
            var host = new NpgsqlConnectionStringBuilder(db.HostConnectionString);
            builder.Username = host.Username;
            builder.Password = host.Password;
        }
        else
        {
            builder.Username = original.Username;
            builder.Password = original.Password;
        }

        var updated = builder.ConnectionString;
        if (!string.Equals(updated, npgsql.ConnectionString, StringComparison.Ordinal))
        {
            npgsql.ConnectionString = updated;
        }
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var db = options.Value.Database;
        var bypass = !db.EnableRowLevelSecurity || (tenantContext.IsHostScope && db.RlsBypass == RlsBypassMode.SessionVariable);
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
