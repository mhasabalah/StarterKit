using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts.Push;
using Shared.Contracts.Localization;
using Shared.Contracts.Mediation;
using UserDirectory.Contracts.Queries;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

public sealed class PushDeliveryJobTests
{
    private readonly FakePushDeviceRepository _devices = new();
    private readonly FakeNotificationLogRepository _logs = new();
    private readonly FakePushProvider _provider = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<INotificationPreferenceRepository> _preferences = new();
    private readonly Mock<ISender> _sender = new();
    private readonly List<(Job Job, IState State)> _scheduled = [];
    private readonly PushOptions _options = new() { MaxAttempts = 3, RetryDelaysSeconds = [30, 120] };
    private readonly Guid _user = Guid.NewGuid();
    private HashSet<Guid> _activeSessions = [];
    private DeliveryPreference? _preference;

    public PushDeliveryJobTests()
    {
        _preferences.Setup(p => p.GetDeliveryPreferenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _preference);
        _sender.Setup(s => s.Send(It.IsAny<GetActiveSessionIdsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _activeSessions);
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, state) => _scheduled.Add((job, state)))
            .Returns("job-1");
    }

    private PushDeliveryJob Job() => new(
        _sender.Object, _devices, _preferences.Object, _logs, _provider, _jobs.Object, MsOptions.Create(_options),
        new NotificationMetrics(), new ManualTimeProvider(DateTimeOffset.UtcNow), NullLogger<PushDeliveryJob>.Instance);

    private PushDelivery Delivery(bool sensitive = false, IReadOnlyList<Guid>? devices = null, int attempt = 1,
        NotificationCulture culture = NotificationCulture.En) =>
        new(_user, culture, new PushContent(
            new LocalizedText("Payment received", "تم استلام الدفعة"),
            new LocalizedText("We received 100 EGP.", "استلمنا 100 جنيه."),
            new Dictionary<string, string> { ["invoiceId"] = "7", ["category"] = "billing.paid" },
            sensitive), devices, attempt);

    [Fact]
    public async Task Every_active_device_gets_the_message_collapsed_on_the_correlation_id()
    {
        var sid = Guid.NewGuid();
        _activeSessions = [sid];
        var phone = _devices.Add(_user, sid);
        var tablet = _devices.Add(_user, sessionId: null, PushPlatform.Ios);
        var correlation = Guid.NewGuid();

        await Job().ExecuteAsync(Delivery(), correlation);

        var call = _provider.Calls.Should().ContainSingle().Subject;
        call.Targets.Select(t => t.DeviceId).Should().BeEquivalentTo([phone.Id, tablet.Id]);
        call.Message.Title.Should().Be("Payment received");
        call.Message.CollapseKey.Should().Be(correlation.ToString("N"));
        call.Message.Data.Should().Contain("correlationId", correlation.ToString("D")).And.Contain("invoiceId", "7");
        call.Message.TimeToLive.Should().Be(_options.DefaultTimeToLive);
        _logs.Rows.Should().ContainSingle(r => r.Channel == NotificationChannel.Push && r.Status == NotificationDeliveryStatus.Sent
                                               && r.Error == null && r.TemplateName == "billing.paid");
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task Devices_of_a_signed_out_or_revoked_session_are_deleted_and_never_sent_to()
    {
        var live = Guid.NewGuid();
        _activeSessions = [live];
        var current = _devices.Add(_user, live);
        var lostPhone = _devices.Add(_user, Guid.NewGuid());

        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _provider.Calls.Single().Targets.Should().ContainSingle(t => t.DeviceId == current.Id);
        _devices.Rows.Should().NotContain(d => d.Id == lostPhone.Id);
    }

    [Fact]
    public async Task No_session_lookup_happens_when_no_device_is_session_bound()
    {
        _devices.Add(_user, sessionId: null);

        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _sender.Verify(s => s.Send(It.IsAny<GetActiveSessionIdsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _provider.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Push_turned_off_is_skipped_and_logged()
    {
        _devices.Add(_user);
        _preference = new DeliveryPreference(false, new Dictionary<string, ChannelMuteSet>(), null, null, null);

        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _provider.Calls.Should().BeEmpty();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task No_device_is_skipped_and_logged()
    {
        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _provider.Calls.Should().BeEmpty();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped && r.Error!.Contains("No active push device"));
    }

    [Fact]
    public async Task A_recipient_without_a_user_is_skipped()
    {
        await Job().ExecuteAsync(Delivery() with { UserId = Guid.Empty }, Guid.NewGuid());

        _provider.Calls.Should().BeEmpty();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task Dead_tokens_are_deleted_and_the_rest_still_counts_as_Sent()
    {
        var good = _devices.Add(_user);
        var dead = _devices.Add(_user);
        _provider.Outcome = t => t.DeviceId == dead.Id ? PushSendOutcome.InvalidToken : PushSendOutcome.Delivered;

        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _devices.Rows.Should().ContainSingle(d => d.Id == good.Id);
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent && r.Error!.Contains("1 dead token"));
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejected_messages_keep_the_device_and_are_not_retried()
    {
        var device = _devices.Add(_user);
        _provider.Outcome = _ => PushSendOutcome.Rejected;

        await Job().ExecuteAsync(Delivery(), Guid.NewGuid());

        _devices.Rows.Should().ContainSingle(d => d.Id == device.Id, "INVALID_ARGUMENT may be the payload: never wipe devices on it");
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Failed && r.Error!.StartsWith("[permanent]"));
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task Transient_failures_retry_ONLY_the_failed_devices_with_backoff()
    {
        var delivered = _devices.Add(_user);
        var throttled = _devices.Add(_user);
        _provider.Outcome = t => t.DeviceId == throttled.Id ? PushSendOutcome.Transient : PushSendOutcome.Delivered;
        var correlation = Guid.NewGuid();

        await Job().ExecuteAsync(Delivery(), correlation);

        var (job, state) = _scheduled.Should().ContainSingle().Subject;
        job.Type.Should().Be<PushDeliveryJob>();
        var next = job.Args[0].Should().BeOfType<PushDelivery>().Subject;
        next.DeviceIds.Should().Equal(throttled.Id);
        next.Attempt.Should().Be(2);
        job.Args[1].Should().Be(correlation);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(10));
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent && r.Error!.Contains("retry scheduled"));
    }

    [Fact]
    public async Task The_follow_up_attempt_sends_only_to_its_devices()
    {
        var first = _devices.Add(_user);
        var second = _devices.Add(_user);

        await Job().ExecuteAsync(Delivery(devices: [second.Id], attempt: 2), Guid.NewGuid());

        _provider.Calls.Single().Targets.Should().ContainSingle(t => t.DeviceId == second.Id);
        _ = first;
    }

    [Fact]
    public async Task After_the_last_attempt_transient_failures_are_given_up_and_logged_Failed()
    {
        var device = _devices.Add(_user);
        _provider.Outcome = _ => PushSendOutcome.Transient;

        await Job().ExecuteAsync(Delivery(devices: [device.Id], attempt: _options.MaxAttempts), Guid.NewGuid());

        _scheduled.Should().BeEmpty();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Failed
                                               && r.Error!.StartsWith("[transient]") && r.Error.Contains("attempts exhausted"));
        _devices.Rows.Should().ContainSingle("a throttled device is not a dead one");
    }

    [Fact]
    public async Task A_follow_up_after_the_device_was_removed_does_nothing()
    {
        await Job().ExecuteAsync(Delivery(devices: [Guid.NewGuid()], attempt: 2), Guid.NewGuid());

        _provider.Calls.Should().BeEmpty();
        _logs.Rows.Should().BeEmpty("only the first attempt reports 'no device'");
    }

    [Fact]
    public async Task Sensitive_messages_send_generic_localized_text_only()
    {
        _devices.Add(_user);

        await Job().ExecuteAsync(Delivery(sensitive: true, culture: NotificationCulture.Ar), Guid.NewGuid());

        var message = _provider.Calls.Single().Message;
        message.Title.Should().Be(PushDeliveryJob.SensitiveTitle.Ar);
        message.Body.Should().Be(PushDeliveryJob.SensitiveBody.Ar);
        message.Title.Should().NotContain("الدفعة");
    }

    [Theory]
    [InlineData(0, 1)]       // not set -> default (1 day)
    [InlineData(3600, 3600)] // explicit
    [InlineData(40 * 86400, 28 * 86400)] // FCM maximum
    public void Time_to_live_is_defaulted_and_capped(int requestedSeconds, int expectedSeconds)
    {
        var delivery = Delivery() with
        {
            Content = Delivery().Content with { TimeToLive = requestedSeconds == 0 ? null : TimeSpan.FromSeconds(requestedSeconds) }
        };

        var message = PushDeliveryJob.Render(delivery, Guid.NewGuid(), new PushOptions { DefaultTimeToLive = TimeSpan.FromSeconds(1) });

        message.TimeToLive.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
