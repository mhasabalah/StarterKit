using Microsoft.EntityFrameworkCore;
using Shared.Core.Data;
using Testcontainers.PostgreSql;

namespace Notification.Tests.Persistence;

/// <summary>
/// The real migration (<c>InitialNotification</c>) applied to PostgreSQL, and the repository queries that only a real
/// provider can prove (grouped counts, batched deletes).
/// </summary>
public sealed class NotificationLogRepositoryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<NotificationDbContext> _options = null!;

    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Exactly the production configuration of AddCustomDbContext (timestamptz, no warning suppression, so a
        // model/migration drift makes MigrateAsync throw here too).
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(_container.GetConnectionString(), b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new NotificationDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private NotificationLogRepository Repository(NotificationDbContext db) => new(db);

    private async Task SeedAsync(params NotificationLog[] rows)
    {
        await using var db = new NotificationDbContext(_options);
        db.NotificationLogs.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static NotificationLog Row(Guid correlation, NotificationDeliveryStatus status, DateTime createdAt,
        NotificationChannel channel = NotificationChannel.Email) => new()
    {
        Id = Guid.NewGuid(),
        CorrelationId = correlation,
        UserId = Guid.NewGuid(),
        Channel = channel,
        TemplateName = "WelcomeUser",
        Status = status,
        CreatedAt = createdAt
    };

    [Fact]
    public async Task Migration_creates_the_schema_and_its_history_table()
    {
        await using var db = new NotificationDbContext(_options);

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().ContainSingle().Which.Should().EndWith("_InitialNotification");
        (await db.NotificationLogs.CountAsync()).Should().Be(0);
        (await db.UserNotificationPreferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Undelivered_counts_notifications_that_failed_and_were_never_sent_in_the_window()
    {
        var since = Now.AddHours(-24);
        Guid failedOnly = Guid.NewGuid(), fellBackAndSent = Guid.NewGuid(), retriedTwice = Guid.NewGuid(),
            queued = Guid.NewGuid(), tooOld = Guid.NewGuid();

        await SeedAsync(
            Row(failedOnly, NotificationDeliveryStatus.Failed, Now.AddHours(-1)),
            Row(fellBackAndSent, NotificationDeliveryStatus.Failed, Now.AddHours(-2), NotificationChannel.WhatsApp),
            Row(fellBackAndSent, NotificationDeliveryStatus.Sent, Now.AddHours(-2).AddMinutes(1)),
            Row(retriedTwice, NotificationDeliveryStatus.Failed, Now.AddHours(-3)),
            Row(retriedTwice, NotificationDeliveryStatus.Failed, Now.AddHours(-2)),
            Row(queued, NotificationDeliveryStatus.Queued, Now.AddMinutes(-5)),
            Row(tooOld, NotificationDeliveryStatus.Failed, Now.AddHours(-30)));

        await using var db = new NotificationDbContext(_options);

        (await Repository(db).CountUndeliveredSinceAsync(since)).Should().Be(2, "failedOnly + retriedTwice (counted once)");
    }

    [Fact]
    public async Task Purge_deletes_only_older_rows_in_batches()
    {
        var old = Enumerable.Range(0, 5).Select(i => Row(Guid.NewGuid(), NotificationDeliveryStatus.Sent, Now.AddDays(-100 - i))).ToArray();
        var recent = Row(Guid.NewGuid(), NotificationDeliveryStatus.Sent, Now.AddDays(-1));
        await SeedAsync([.. old, recent]);

        await using var db = new NotificationDbContext(_options);
        var deleted = await Repository(db).PurgeOlderThanAsync(Now.AddDays(-90), batchSize: 2);

        deleted.Should().Be(5);
        (await db.NotificationLogs.Select(x => x.Id).ToListAsync()).Should().Equal(recent.Id);
    }

    [Fact]
    public async Task Correlation_history_is_ordered_and_omits_the_message_payload()
    {
        var correlation = Guid.NewGuid();
        var first = Row(correlation, NotificationDeliveryStatus.Failed, Now.AddMinutes(-10));
        first.MessageJson = """{"userId":"x"}""";
        var second = Row(correlation, NotificationDeliveryStatus.Sent, Now.AddMinutes(-5));
        await SeedAsync(second, first);

        await using var db = new NotificationDbContext(_options);
        var history = await Repository(db).GetByCorrelationAsync(correlation);

        history.Select(h => h.Id).Should().Equal(first.Id, second.Id);
        history.Should().OnlyContain(h => h.MessageJson == null);
    }
}
