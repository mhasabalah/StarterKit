using Microsoft.Extensions.Logging;
using Npgsql;

namespace Shared.Core.Data;

/// <summary>
/// Serializes concurrent migrators (several replicas, or a deploy-time migrator racing a booting app) with a Postgres
/// SESSION advisory lock on a dedicated, unpooled connection: the lock is per database and is released when the
/// connection closes, even if the process dies.
/// </summary>
public sealed class MigrationLock : IAsyncDisposable
{
    /// <summary>Arbitrary constant key ("STARTERKIT" in ASCII) shared by every migrator of a database.</summary>
    public const long LockKey = 0x5354415254455249;

    private readonly NpgsqlConnection? _connection;

    private MigrationLock(NpgsqlConnection? connection) => _connection = connection;

    /// <summary>Waits up to <paramref name="timeout"/> for the lock; no-op for an empty connection string.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last",
        Justification = "Public signature used by the host and tests; the trailing optional TimeProvider is a test seam.")]
    public static async Task<MigrationLock> AcquireAsync(string? connectionString, TimeSpan timeout, ILogger logger,
        CancellationToken cancellationToken = default, TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new MigrationLock(null);
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            var deadline = timeProvider.GetUtcNow() + timeout;
            var announced = false;
            while (true)
            {
                await using var command = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
                {
                    return new MigrationLock(connection);
                }

                if (!announced)
                {
                    logger.LogInformation("Another migrator holds the migration lock; waiting up to {Seconds}s",
                        (int)timeout.TotalSeconds);
                    announced = true;
                }

                if (timeProvider.GetUtcNow() >= deadline)
                {
                    throw new TimeoutException(
                        $"Could not acquire the migration advisory lock within {(int)timeout.TotalSeconds}s.");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await using var command = new NpgsqlCommand("select pg_advisory_unlock(@key)", _connection);
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteScalarAsync();
        }
        catch
        {
            // Closing the (unpooled) session releases the lock anyway.
        }

        await _connection.DisposeAsync();
    }
}
