using Microsoft.Extensions.Logging;

namespace Notification.Tests.Dispatch;

public sealed class NotificationDailyFailureSummaryJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);

    private static (NotificationDailyFailureSummaryJob Job, RecordingLogger Log) Create(FakeNotificationLogRepository repository)
    {
        var log = new RecordingLogger();
        return (new NotificationDailyFailureSummaryJob(repository, new FixedTimeProvider(Now), log), log);
    }

    private static NotificationLog Row(Guid correlation, NotificationDeliveryStatus status, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), CorrelationId = correlation, Status = status, CreatedAt = at.UtcDateTime
    };

    [Fact]
    public async Task Warns_with_the_number_of_undelivered_notifications_of_the_last_24_hours()
    {
        var repository = new FakeNotificationLogRepository();
        var failed = Guid.NewGuid();
        var recovered = Guid.NewGuid();
        repository.Rows.AddRange(
        [
            Row(failed, NotificationDeliveryStatus.Failed, Now.AddHours(-2)),
            Row(recovered, NotificationDeliveryStatus.Failed, Now.AddHours(-3)),
            Row(recovered, NotificationDeliveryStatus.Sent, Now.AddHours(-3).AddMinutes(1)),
            Row(Guid.NewGuid(), NotificationDeliveryStatus.Failed, Now.AddHours(-25))
        ]);
        var (job, log) = Create(repository);

        await job.ExecuteAsync();

        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("1 notification(s)"));
    }

    [Fact]
    public async Task Logs_information_when_everything_was_delivered()
    {
        var (job, log) = Create(new FakeNotificationLogRepository());

        await job.ExecuteAsync();

        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger : ILogger<NotificationDailyFailureSummaryJob>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
