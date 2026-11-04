using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Startup warning: RLS depends on SESSION-level <c>set_config</c> values (<see cref="TenantSessionConnectionInterceptor"/>),
/// which are unsafe behind a transaction-pooling proxy (PgBouncer <c>pool_mode=transaction</c>): the values may land on
/// a different server connection than the query. A cheap heuristic only (port 6432 or a host containing "pgbouncer"),
/// since the pool mode itself is not visible to the client. Use session pooling or direct connections.
/// </summary>
public sealed class PoolingSafetyCheck(
    IConfiguration configuration,
    IOptions<MultiTenancyOptions> options,
    ILogger<PoolingSafetyCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Database.EnableRowLevelSecurity) return Task.CompletedTask;

        var candidates = configuration.GetSection("ConnectionStrings").GetChildren()
            .Concat(configuration.GetSection(options.Value.Database.ConnectionsSection).GetChildren())
            .Select(c => (Name: c.Key, c.Value))
            .Append((Name: "MultiTenancy:Database:HostConnectionString", Value: options.Value.Database.HostConnectionString));

        foreach (var (name, value) in candidates)
        {
            if (LooksLikeTransactionPooler(value))
            {
                logger.LogWarning(
                    "Connection '{Name}' looks like a PgBouncer endpoint. Tenant row-level security uses session-level " +
                    "settings and is NOT safe behind transaction pooling; use session pooling or a direct connection " +
                    "(see wiki/MultiTenancy/Isolation-and-Dedicated-Databases.md).", name);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>True for port 6432 or a host name containing "pgbouncer". Unparseable values are ignored.</summary>
    public static bool LooksLikeTransactionPooler(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return builder.Port == 6432 ||
                   (builder.Host?.Contains("pgbouncer", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }
}
