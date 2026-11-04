using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Data;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Infrastructure;

public sealed class MigrationLockTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _db.StartAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Second_migrator_waits_until_the_first_releases_the_lock()
    {
        var first = await MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromSeconds(5),
            NullLogger.Instance);

        var second = MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromSeconds(10),
            NullLogger.Instance);
        await Task.Delay(1500);
        second.IsCompleted.Should().BeFalse("the first migrator still holds the lock");

        await first.DisposeAsync();
        await using var acquired = await second;
    }

    [Fact]
    public async Task Acquire_times_out_while_the_lock_is_held()
    {
        await using var held = await MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromSeconds(5),
            NullLogger.Instance);

        var act = () => MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromSeconds(1),
            NullLogger.Instance);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Acquire_deadline_follows_the_injected_TimeProvider()
    {
        await using var held = await MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromSeconds(5),
            NullLogger.Instance);
        var clock = new JumpingTimeProvider();

        // A 1-hour timeout elapses at once on the fake clock: no real waiting, so this returns promptly.
        var act = () => MigrationLock.AcquireAsync(_db.GetConnectionString(), TimeSpan.FromHours(1),
            NullLogger.Instance, timeProvider: clock);

        await act.Should().ThrowAsync<TimeoutException>();
        clock.Reads.Should().BeGreaterThanOrEqualTo(2);
    }

    private sealed class JumpingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            var current = _now;
            _now = _now.AddHours(2);
            return current;
        }
    }

    [Fact]
    public async Task Empty_connection_string_is_a_no_op()
    {
        await using var l = await MigrationLock.AcquireAsync(null, TimeSpan.FromSeconds(1), NullLogger.Instance);
    }

    [Fact]
    public void Options_default_to_migrating_on_startup_and_respect_configuration()
    {
        DatabaseMigrationOptions.From(new ConfigurationBuilder().Build()).MigrateOnStartup.Should().BeTrue();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false"
        }).Build();
        var options = DatabaseMigrationOptions.From(config);
        options.ShouldMigrate.Should().BeFalse();

        options.MigrateOnly = true;
        options.ShouldMigrate.Should().BeTrue("migrate-only always migrates");
    }
}
