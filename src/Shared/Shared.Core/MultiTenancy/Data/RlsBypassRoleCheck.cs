using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Core.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Startup guard for <see cref="RlsBypassMode.Role"/>: the policies only stop the runtime role from bypassing RLS via
/// <c>app.bypass_rls</c> when the group role <see cref="TenantRlsMigrationExtensions.BypassRoleName"/> exists. Without
/// it, <c>Role</c> mode would LOOK hardened but still accept the variable, so the app refuses to start.
/// An unreachable database is only logged here (migrations / health checks report it); a reachable database
/// without the role is fatal. It also connects with the RUNTIME connection string (the one the tenant-data contexts
/// use) and refuses to start when that role is a superuser, has <c>BYPASSRLS</c> or is a member of the bypass role:
/// each of those makes RLS (and the host-scope credential swap) meaningless.
/// </summary>
public sealed class RlsBypassRoleCheck(
    IOptions<MultiTenancyOptions> options,
    ILogger<RlsBypassRoleCheck> logger,
    IConfiguration? configuration = null,
    PostgresOptions? postgres = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var db = options.Value.Database;
        if (db.RlsBypass != RlsBypassMode.Role || !db.EnableRowLevelSecurity ||
            string.IsNullOrWhiteSpace(db.HostConnectionString))
        {
            return;
        }

        bool exists;
        try
        {
            await using var connection = new NpgsqlConnection(db.HostConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = @role)", connection);
            command.Parameters.AddWithValue("role", TenantRlsMigrationExtensions.BypassRoleName);
            exists = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex,
                "Could not verify that the '{Role}' group role exists (RlsBypass=Role). Verify it before serving traffic.",
                TenantRlsMigrationExtensions.BypassRoleName);
            return;
        }

        await VerifyRuntimeRoleAsync(cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException(
                $"MultiTenancy:Database:RlsBypass=Role but the Postgres group role '{TenantRlsMigrationExtensions.BypassRoleName}' " +
                "does not exist, so the runtime role could still bypass RLS. Run deployment/postgres/scripts/init-rls-roles.sql " +
                "(see wiki/MultiTenancy/Isolation-and-Dedicated-Databases.md).");
        }
    }

    private async Task VerifyRuntimeRoleAsync(CancellationToken cancellationToken)
    {
        var runtime = configuration?.GetConnectionString(Shared.Core.Data.DatabaseDefaults.ConnectionStringName) ?? postgres?.ConnectionString;
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return;
        }

        bool superuser, bypassRls, member;
        string user;
        try
        {
            await using var connection = new NpgsqlConnection(runtime);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT current_user::text, r.rolsuper, r.rolbypassrls, " +
                "COALESCE((SELECT bool_or(pg_catalog.pg_has_role(current_user, g.oid, 'MEMBER')) " +
                "FROM pg_catalog.pg_roles g WHERE g.rolname = @role), false) " +
                "FROM pg_catalog.pg_roles r WHERE r.rolname = current_user", connection);
            command.Parameters.AddWithValue("role", TenantRlsMigrationExtensions.BypassRoleName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            user = reader.GetString(0);
            superuser = reader.GetBoolean(1);
            bypassRls = reader.GetBoolean(2);
            member = reader.GetBoolean(3);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not verify the runtime database role privileges (RlsBypass=Role).");
            return;
        }

        if (superuser || bypassRls || member)
        {
            throw new InvalidOperationException(
                $"MultiTenancy:Database:RlsBypass=Role but the runtime role '{user}' can bypass RLS " +
                $"(superuser={superuser}, bypassrls={bypassRls}, member of {TenantRlsMigrationExtensions.BypassRoleName}={member}). " +
                "The runtime role must be a plain non-superuser without BYPASSRLS and outside the bypass group role.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
