using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Core.MultiTenancy;

/// <summary>
/// Default <see cref="ITenantConnectionStringResolver"/>:
/// <list type="bullet">
/// <item>Shared tenants, host scope, unresolved tenant, or multi-tenancy disabled → the shared connection string.</item>
/// <item>Dedicated tenant with <see cref="TenantInfo.ConnectionSecretRef"/> → <c>{ConnectionsSection}:{ref}</c> or
/// <c>ConnectionStrings:{ref}</c> (configuration / Docker secrets - never stored in the database).</item>
/// <item>Dedicated tenant without a ref → the shared server with database
/// <see cref="TenantDatabaseOptions.DedicatedDatabaseNameFormat"/>.</item>
/// </list>
/// A dedicated tenant whose connection cannot be resolved FAILS (never silently falls back to the shared database).
/// </summary>
/// <param name="options">Multi-tenancy options.</param>
/// <param name="configuration">Needed only for dedicated tenants with a <c>ConnectionSecretRef</c> (optional so hosts
/// and test containers without configuration still resolve shared connections).</param>
public sealed class TenantConnectionStringResolver(
    IOptions<MultiTenancyOptions> options,
    IConfiguration? configuration = null) : ITenantConnectionStringResolver
{
    private readonly ConcurrentDictionary<(Guid, string?, string), string> _cache = new();

    public string Resolve(TenantInfo? tenant, string sharedConnectionString)
    {
        if (tenant is null || !tenant.IsDedicated || !options.Value.Enabled)
        {
            return sharedConnectionString;
        }

        return _cache.GetOrAdd((tenant.Id, tenant.ConnectionSecretRef, sharedConnectionString),
            _ => Build(tenant, sharedConnectionString));
    }

    /// <summary>Clears cached connection strings (call after rotating a dedicated tenant's secret).</summary>
    public void Invalidate(Guid tenantId)
    {
        foreach (var key in _cache.Keys.Where(k => k.Item1 == tenantId))
        {
            _cache.TryRemove(key, out _);
        }
    }

    private string Build(TenantInfo tenant, string sharedConnectionString)
    {
        var db = options.Value.Database;
        string raw;

        if (!string.IsNullOrWhiteSpace(tenant.ConnectionSecretRef))
        {
            raw = configuration?[$"{db.ConnectionsSection}:{tenant.ConnectionSecretRef}"]
                  ?? configuration?.GetConnectionString(tenant.ConnectionSecretRef)
                  ?? throw new InvalidOperationException(
                      $"Dedicated tenant '{tenant.Identifier}' references connection '{tenant.ConnectionSecretRef}', " +
                      $"which is not configured ({db.ConnectionsSection}:{tenant.ConnectionSecretRef} or " +
                      $"ConnectionStrings:{tenant.ConnectionSecretRef}).");
        }
        else
        {
            raw = DedicatedDatabaseConnectionString(sharedConnectionString, tenant.Identifier, db.DedicatedDatabaseNameFormat);
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (db.MaxDedicatedPoolSize > 0 && builder.MaxPoolSize > db.MaxDedicatedPoolSize)
        {
            builder.MaxPoolSize = db.MaxDedicatedPoolSize;
        }

        return builder.ConnectionString;
    }

    /// <summary>The shared connection string pointed at the tenant's own database name.</summary>
    public static string DedicatedDatabaseConnectionString(string sharedConnectionString, string identifier, string format)
    {
        var builder = new NpgsqlConnectionStringBuilder(sharedConnectionString);
        builder.Database = DedicatedDatabaseName(builder.Database ?? "postgres", identifier, format);
        return builder.ConnectionString;
    }

    /// <summary>Postgres-safe database name (lowercase letters, digits, underscore; max 63 chars).</summary>
    public static string DedicatedDatabaseName(string sharedDatabase, string identifier, string format)
    {
        var name = format
            .Replace("{database}", sharedDatabase, StringComparison.Ordinal)
            .Replace("{identifier}", identifier, StringComparison.Ordinal)
            .ToLowerInvariant();

        var safe = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        return safe.Length <= 63 ? safe : safe[..63];
    }
}
