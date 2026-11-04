using System.Net;
using UserDirectory.Contracts.Queries;
using Shared.Contracts.Mediation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly.Timeout;

namespace Notification.Tests.Dispatch;

public sealed class NotificationDispatchJobTests
{
    private readonly FakeNotificationLogRepository _logs = new();
    private readonly FakeChannelAdapter _whatsApp = new(NotificationChannel.WhatsApp);
    private readonly FakeChannelAdapter _email = new(NotificationChannel.Email);
    private readonly NotificationOptions _options = new() { FallbackAfterAttempts = 3 };
    private readonly Guid _userId = Guid.NewGuid();
    private string? _phone = "+201001234567";

    private NotificationDispatchJob CreateJob(params NotificationChannel[] order)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new UserContactInfoDto("user@example.com", _phone));

        var preferences = new Mock<INotificationPreferenceRepository>();
        preferences.Setup(p => p.ResolveChannelOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order.Length > 0 ? order : [NotificationChannel.WhatsApp, NotificationChannel.Email]);

        return new NotificationDispatchJob(
            sender.Object,
            _logs,
            preferences.Object,
            new INotificationChannelAdapter[] { _whatsApp, _email },
            Microsoft.Extensions.Options.Options.Create(_options),
            NullLogger<NotificationDispatchJob>.Instance);
    }

    private NotificationMessage Message(EmailAttachment[]? attachments = null) => new()
    {
        UserId = _userId,
        Email = new EmailPayload(EmailTemplate.WelcomeUser, new Dictionary<string, object> { ["firstName"] = "A" }, attachments),
        WhatsApp = new WhatsAppPayload(WhatsAppTemplate.SubscriptionActivated)
    };

    private static WhatsAppApiException Http(HttpStatusCode code, string body = "{\"error\":{\"code\":132001}}")
        => new(code, body);

    [Fact]
    public async Task Delivers_on_the_first_channel_and_stops()
    {
        var job = CreateJob();

        await job.ExecuteAsync(Message(), Guid.NewGuid());

        _whatsApp.Sends.Should().Be(1);
        _email.Attempts.Should().Be(0);
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent && r.Channel == NotificationChannel.WhatsApp);
    }

    [Fact]
    public async Task Falls_back_in_channel_order_after_a_permanent_failure()
    {
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.BadRequest);
        var job = CreateJob(NotificationChannel.WhatsApp, NotificationChannel.Email);

        await job.ExecuteAsync(Message(), Guid.NewGuid());

        _email.Sends.Should().Be(1);
        _logs.Rows.Select(r => (r.Channel, r.Status)).Should().Equal(
            (NotificationChannel.WhatsApp, NotificationDeliveryStatus.Failed),
            (NotificationChannel.Email, NotificationDeliveryStatus.Sent));
    }

    [Fact]
    public async Task Respects_a_custom_order()
    {
        var job = CreateJob(NotificationChannel.Email, NotificationChannel.WhatsApp);

        await job.ExecuteAsync(Message(), Guid.NewGuid());

        _email.Sends.Should().Be(1);
        _whatsApp.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Rerun_with_the_same_correlation_does_not_resend_a_sent_channel()
    {
        var job = CreateJob();
        var correlationId = Guid.NewGuid();

        await job.ExecuteAsync(Message(), correlationId);
        await job.ExecuteAsync(Message(), correlationId);
        await job.ExecuteAsync(Message(), correlationId);

        _whatsApp.Sends.Should().Be(1);
        _email.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Rerun_after_fallback_success_does_not_retry_any_channel()
    {
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.NotFound);
        var job = CreateJob();
        var correlationId = Guid.NewGuid();

        await job.ExecuteAsync(Message(), correlationId);
        await job.ExecuteAsync(Message(), correlationId);

        _whatsApp.Attempts.Should().Be(1);
        _email.Sends.Should().Be(1);
    }

    [Fact]
    public async Task Permanent_4xx_is_not_retried_and_the_provider_body_is_logged()
    {
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Template name does not exist\"}}");
        var job = CreateJob();

        // Forced single channel: nothing to fall back to, and the job must NOT throw (no Hangfire retry).
        var act = () => job.ExecuteAsync(Message(), Guid.NewGuid(), NotificationChannel.WhatsApp);

        await act.Should().NotThrowAsync();
        _whatsApp.Attempts.Should().Be(1);
        var row = _logs.Rows.Single();
        row.Status.Should().Be(NotificationDeliveryStatus.Failed);
        row.Error.Should().StartWith("[permanent]").And.Contain("Template name does not exist");
    }

    [Fact]
    public async Task Transient_5xx_is_retried_on_the_same_channel_without_fallback()
    {
        _whatsApp.Failures.Enqueue(Http(HttpStatusCode.ServiceUnavailable));
        var job = CreateJob();
        var correlationId = Guid.NewGuid();

        var first = () => job.ExecuteAsync(Message(), correlationId);
        (await first.Should().ThrowAsync<NotificationDeliveryException>())
            .Which.Kind.Should().Be(DeliveryFailureKind.Transient);
        _email.Attempts.Should().Be(0);

        // Hangfire retry: WhatsApp recovers.
        await job.ExecuteAsync(Message(), correlationId);

        _whatsApp.Sends.Should().Be(1);
        _email.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Transient_failures_fall_back_after_the_configured_attempts()
    {
        _options.FallbackAfterAttempts = 2;
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.InternalServerError);
        var job = CreateJob();
        var correlationId = Guid.NewGuid();

        await FluentActions.Invoking(() => job.ExecuteAsync(Message(), correlationId))
            .Should().ThrowAsync<NotificationDeliveryException>();

        await job.ExecuteAsync(Message(), correlationId); // 2nd attempt reaches the limit -> fallback

        _whatsApp.Attempts.Should().Be(2);
        _email.Sends.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(TimeoutExceptions))]
    public async Task Timeout_never_falls_back_to_another_channel(Exception timeout)
    {
        _options.FallbackAfterAttempts = 1;
        _whatsApp.AlwaysThrow = timeout;
        var job = CreateJob();
        var correlationId = Guid.NewGuid();

        for (var run = 0; run < 4; run++)
        {
            (await FluentActions.Invoking(() => job.ExecuteAsync(Message(), correlationId))
                    .Should().ThrowAsync<NotificationDeliveryException>())
                .Which.Kind.Should().Be(DeliveryFailureKind.Ambiguous);
        }

        _email.Attempts.Should().Be(0, "the WhatsApp message may already have been delivered");
        _logs.Rows.Should().OnlyContain(r => r.Error!.StartsWith("[ambiguous]"));
    }

    public static TheoryData<Exception> TimeoutExceptions() => new()
    {
        new TaskCanceledException("HttpClient.Timeout"),
        new TimeoutRejectedException("attempt timeout"),
        new TimeoutException()
    };

    [Fact]
    public async Task Missing_contact_is_skipped_and_the_next_channel_used()
    {
        _phone = null;
        var job = CreateJob();

        await job.ExecuteAsync(Message(), Guid.NewGuid());

        _whatsApp.Attempts.Should().Be(0);
        _email.Sends.Should().Be(1);
        _logs.Rows.Should().Contain(r => r.Channel == NotificationChannel.WhatsApp && r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task Attachment_bytes_are_not_persisted_in_message_json()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3, 4 };
        var job = CreateJob(NotificationChannel.Email);

        await job.ExecuteAsync(Message([new EmailAttachment("INV-1.pdf", pdf)]), Guid.NewGuid());

        var json = _logs.Rows.Single().MessageJson!;
        json.Should().Contain("INV-1.pdf");
        json.Should().NotContain(Convert.ToBase64String(pdf));

        var restored = NotificationMessageSerializer.DeserializeFromLog(json)!;
        restored.Email!.Attachments.Should().BeNull("stubs without content are dropped on re-enqueue");
    }

    [Fact]
    public async Task Admin_retry_of_a_permanent_failure_does_not_throw_and_keeps_the_body()
    {
        _whatsApp.AlwaysThrow = Http(HttpStatusCode.BadRequest, "bad number");
        var job = CreateJob(NotificationChannel.WhatsApp);
        await job.ExecuteAsync(Message(), Guid.NewGuid());
        var failed = _logs.Rows.Single();

        await FluentActions.Invoking(() => job.RetryChannelAsync(failed.Id)).Should().NotThrowAsync();

        failed.Status.Should().Be(NotificationDeliveryStatus.Failed);
        failed.Error.Should().Contain("bad number");
    }
}
