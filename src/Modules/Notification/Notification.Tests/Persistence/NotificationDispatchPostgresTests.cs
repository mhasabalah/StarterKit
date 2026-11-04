using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Testcontainers.PostgreSql;
using UserDirectory.Contracts.Queries;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Persistence;

/// <summary>
/// <see cref="NotificationDispatchJob"/> on REAL PostgreSQL (real migration, real log + preference repositories).
/// The delivery guarantees live in the <c>notification_log</c> table, so they are proven against the database rather
/// than the in-memory fake: a delivered notification is never re-sent by a retry, transient attempts are counted
/// across job runs (each run = a fresh DbContext, like a Hangfire retry), fallbacks are recorded, the stored message
/// supports an admin re-enqueue, and the user's channel order comes from the preferences table.
/// Only the channels (programmable fakes) and the contact lookup (<see cref="ISender"/>) are faked.
/// </summary>
public sealed class NotificationDispatchPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<NotificationDbContext> _options = null!;

    private readonly FakeChannelAdapter _whatsApp = new(NotificationChannel.WhatsApp);
    private readonly FakeChannelAdapter _email = new(NotificationChannel.Email);
    private readonly NotificationOptions _notificationOptions = new()
    {
        FallbackAfterAttempts = 2,
        DefaultChannelOrder = [NotificationChannel.WhatsApp, NotificationChannel.Email]
    };
    private readonly Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(_container.GetConnectionString(), b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new NotificationDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>One job run in its own DbContext - what a Hangfire (re)try gets.</summary>
    private async Task RunAsync(NotificationMessage message, Guid correlationId)
    {
        await using var db = new NotificationDbContext(_options);
        await Job(db).ExecuteAsync(message, correlationId);
    }

    private NotificationDispatchJob Job(NotificationDbContext db)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserContactInfoDto("user@example.com", "+201001234567"));

        return new NotificationDispatchJob(
            sender.Object,
            new NotificationLogRepository(db),
            new NotificationPreferenceRepository(db, MsOptions.Create(_notificationOptions)),
            new INotificationChannelAdapter[] { _whatsApp, _email },
            MsOptions.Create(_notificationOptions),
            NullLogger<NotificationDispatchJob>.Instance);
    }

    private NotificationMessage Message() => new()
    {
        UserId = _userId,
        Email = new EmailPayload(EmailTemplate.WelcomeUser, new Dictionary<string, object> { ["firstName"] = "A" }, null),
        WhatsApp = new WhatsAppPayload(WhatsAppTemplate.SubscriptionActivated)
    };

    private async Task<List<NotificationLog>> RowsAsync(Guid correlationId)
    {
        await using var db = new NotificationDbContext(_options);
        return await db.NotificationLogs.AsNoTracking()
            .Where(r => r.CorrelationId == correlationId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    private static WhatsAppApiException Http(HttpStatusCode code) => new(code, "{\"error\":{\"code\":132001}}");

    [Fact]
    public async Task Delivered_notification_is_persisted_as_Sent_and_a_rerun_never_sends_it_again()
    {
        var correlationId = Guid.NewGuid();

        await RunAsync(Message(), correlationId);
        await RunAsync(Message(), correlationId); // e.g. Hangfire re-executing after a worker crash

        _whatsApp.Sends.Should().Be(1, "the Sent row in the database makes the re-run a no-op");
        var row = (await RowsAsync(correlationId)).Should().ContainSingle().Subject;
        row.Status.Should().Be(NotificationDeliveryStatus.Sent);
        row.Channel.Should().Be(NotificationChannel.WhatsApp);
        row.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Permanent_failure_falls_back_and_both_attempts_are_recorded()
    {
        var correlationId = Guid.NewGuid();
        _whatsApp.Failures.Enqueue(Http(HttpStatusCode.BadRequest));

        await RunAsync(Message(), correlationId);

        var rows = await RowsAsync(correlationId);
        rows.Select(r => (r.Channel, r.Status)).Should().Equal(
            (NotificationChannel.WhatsApp, NotificationDeliveryStatus.Failed),
            (NotificationChannel.Email, NotificationDeliveryStatus.Sent));
        rows[0].Error.Should().NotBeNullOrWhiteSpace();
        _email.Sends.Should().Be(1);
    }

    [Fact]
    public async Task Transient_attempts_are_counted_across_runs_then_fall_back()
    {
        var correlationId = Guid.NewGuid();
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.ServiceUnavailable);

        // Run 1: transient -> throw so Hangfire retries the same channel.
        var first = () => RunAsync(Message(), correlationId);
        (await first.Should().ThrowAsync<NotificationDeliveryException>()).Which.Kind.Should().Be(DeliveryFailureKind.Transient);
        _email.Attempts.Should().Be(0, "a transient failure must not fall back before FallbackAfterAttempts");

        // Run 2 (a fresh DbContext reads the first failure back): attempts used up -> fall back to email.
        await RunAsync(Message(), correlationId);

        var rows = await RowsAsync(correlationId);
        rows.Count(r => r.Channel == NotificationChannel.WhatsApp && r.Status == NotificationDeliveryStatus.Failed).Should().Be(2);
        rows.Should().ContainSingle(r => r.Channel == NotificationChannel.Email && r.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task Admin_reenqueue_redelivers_from_the_stored_message()
    {
        var correlationId = Guid.NewGuid();
        _whatsApp.Failures.Enqueue(Http(HttpStatusCode.BadRequest));
        _email.Failures.Enqueue(Http(HttpStatusCode.BadRequest));

        await RunAsync(Message(), correlationId);
        var failed = (await RowsAsync(correlationId)).First(r => r.Channel == NotificationChannel.Email);
        failed.Status.Should().Be(NotificationDeliveryStatus.Failed);
        failed.MessageJson.Should().NotBeNullOrWhiteSpace("the payload is stored for re-enqueueing");

        await using (var db = new NotificationDbContext(_options))
        {
            await Job(db).RetryChannelAsync(failed.Id);
        }

        (await RowsAsync(correlationId)).Single(r => r.Id == failed.Id).Status.Should().Be(NotificationDeliveryStatus.Sent);
        _email.Sends.Should().Be(1);
    }

    [Fact]
    public async Task The_users_stored_channel_order_wins_over_the_default()
    {
        await using (var db = new NotificationDbContext(_options))
        {
            await new NotificationPreferenceRepository(db, MsOptions.Create(_notificationOptions))
                .UpsertAsync(_userId, [NotificationChannel.Email, NotificationChannel.WhatsApp]);
        }

        var correlationId = Guid.NewGuid();
        await RunAsync(Message(), correlationId);

        (await RowsAsync(correlationId)).Should().ContainSingle()
            .Which.Channel.Should().Be(NotificationChannel.Email);
        _whatsApp.Attempts.Should().Be(0);
    }
}
