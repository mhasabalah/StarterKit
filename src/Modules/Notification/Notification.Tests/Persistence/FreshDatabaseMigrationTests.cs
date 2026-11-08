using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shared.Core.Data;
using Testcontainers.PostgreSql;

namespace Notification.Tests.Persistence;

/// <summary>
/// First start on an empty database: EF Core reads a missing history table before creating it and logs the failure
/// at Error level. <see cref="MigrationsHistory.EnsureHistoryTableAsync"/> (called by the startup and tenant migrators)
/// creates the table first, so a clean first start logs no errors.
/// </summary>
public sealed class FreshDatabaseMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Migrating_a_fresh_database_logs_no_errors_and_applies_every_migration()
    {
        var errors = new ErrorLog();
        await using var db = Create(_container.GetConnectionString(), errors);

        await db.Database.EnsureHistoryTableAsync();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();

        errors.Entries.Should().BeEmpty();
        pending.Should().HaveCount(db.Database.GetMigrations().Count()).And.NotBeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Ensuring_the_history_table_of_a_database_that_does_not_exist_yet_leaves_it_to_MigrateAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "tenant_not_created_yet"
        }.ConnectionString;
        await using var db = Create(connection, new ErrorLog());

        await db.Database.EnsureHistoryTableAsync();
        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    private static NotificationDbContext Create(string connectionString, ErrorLog errors) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention()
            .UseLoggerFactory(LoggerFactory.Create(l => l.AddProvider(errors)))
            .Options);

    private sealed class ErrorLog : ILoggerProvider, ILogger
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (Entries)
                {
                    Entries.Add($"{eventId.Name}: {formatter(state, exception)}");
                }
            }
        }
    }
}
