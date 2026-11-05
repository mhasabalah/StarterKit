using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts.InApp;
using Notification.Contracts.Push;
using Shared.Contracts.Localization;
using Shared.Contracts.Mediation;
using UserDirectory.Contracts.Queries;

namespace Notification.Tests.Dispatch;

/// <summary>
/// How one notification fans out into jobs: the email/WhatsApp chain, the inbox and push run as SEPARATE jobs sharing
/// one correlation id, and one never suppresses or re-runs another.
/// </summary>
public sealed class AdditiveChannelDispatchTests
{
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly List<Job> _enqueued = [];
    private readonly FakeNotificationLogRepository _logs = new();
    private readonly Guid _user = Guid.NewGuid();

    private static readonly InAppContent InApp = new(
        new LocalizedText("Welcome", "مرحبًا"), new LocalizedText("Glad you are here.", "سعداء بوجودك."), "account.welcome",
        "/getting-started", NotificationSeverity.Info, new Dictionary<string, string> { ["step"] = "1" });

    private static readonly PushContent Push = new(new LocalizedText("Hi", "أهلًا"), new LocalizedText("Body", "نص"));

    public AdditiveChannelDispatchTests() =>
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => _enqueued.Add(job))
            .Returns(() => $"job-{_enqueued.Count}");

    private NotificationJobEnqueuer Enqueuer() => new(_logs, _jobs.Object, NullLogger<NotificationJobEnqueuer>.Instance);

    private NotificationMessage Message(bool email = true, bool inApp = true, bool push = true) => new()
    {
        UserId = _user,
        Email = email ? new EmailPayload(EmailTemplate.WelcomeUser, []) : null,
        InApp = inApp ? InApp : null,
        Push = push ? Push : null
    };

    private IEnumerable<Type> JobTypes => _enqueued.Select(j => j.Type);

    [Fact]
    public async Task Each_channel_group_gets_its_own_job_and_all_share_the_correlation_id()
    {
        var correlation = await Enqueuer().EnqueueAsync(Message());

        JobTypes.Should().BeEquivalentTo([typeof(NotificationDispatchJob), typeof(InAppDeliveryJob), typeof(PushDeliveryJob)]);
        _enqueued.Should().OnlyContain(j => j.Args.Contains(correlation));

        var chain = (NotificationMessage)_enqueued.Single(j => j.Type == typeof(NotificationDispatchJob)).Args[0];
        chain.InApp.Should().BeNull("the chain job does not carry the additive payloads");
        chain.Push.Should().BeNull();
        ((InAppDelivery)_enqueued.Single(j => j.Type == typeof(InAppDeliveryJob)).Args[0]).Content.Should().Be(InApp);
        ((PushDelivery)_enqueued.Single(j => j.Type == typeof(PushDeliveryJob)).Args[0]).Should()
            .Match<PushDelivery>(p => p.Attempt == 1 && p.DeviceIds == null && p.UserId == _user);
    }

    [Fact]
    public async Task An_in_app_only_notification_runs_no_email_chain()
    {
        await Enqueuer().EnqueueAsync(Message(email: false, push: false));

        JobTypes.Should().Equal(typeof(InAppDeliveryJob));
    }

    [Fact]
    public async Task An_empty_notification_still_reaches_the_dispatch_job_which_logs_it()
    {
        await Enqueuer().EnqueueAsync(new NotificationMessage { UserId = _user });

        JobTypes.Should().Equal(typeof(NotificationDispatchJob));
    }

    [Fact]
    public async Task Forcing_an_additive_channel_delivers_only_that_channel()
    {
        await Enqueuer().EnqueueAsync(Message(), NotificationChannel.Push);

        JobTypes.Should().Equal(typeof(PushDeliveryJob));
    }

    [Fact]
    public async Task Forcing_email_narrows_the_chain_but_keeps_in_app_and_push()
    {
        await Enqueuer().EnqueueAsync(Message(), NotificationChannel.Email);

        JobTypes.Should().BeEquivalentTo([typeof(NotificationDispatchJob), typeof(InAppDeliveryJob), typeof(PushDeliveryJob)]);
        _enqueued.Single(j => j.Type == typeof(NotificationDispatchJob)).Args[2].Should().Be(NotificationChannel.Email);
    }

    [Theory]
    [InlineData(NotificationChannel.InApp)]
    [InlineData(NotificationChannel.Push)]
    public async Task Additive_deliveries_cannot_be_re_enqueued_manually(NotificationChannel channel)
    {
        var log = new NotificationLog
        {
            Id = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), Channel = channel, Status = NotificationDeliveryStatus.Failed,
            MessageJson = "{}"
        };
        await _logs.InsertAsync(log);

        await Enqueuer().Invoking(e => e.ReEnqueueAsync(log.Id))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*retry automatically*");
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task An_in_app_Sent_row_never_stops_the_email_of_the_same_notification()
    {
        // Regression: the dispatch job's "already delivered" check used to look at EVERY row of the correlation id.
        var correlation = Guid.NewGuid();
        await _logs.InsertAsync(new NotificationLog
        {
            Id = Guid.NewGuid(), CorrelationId = correlation, UserId = _user, Channel = NotificationChannel.InApp,
            Status = NotificationDeliveryStatus.Sent, CreatedAt = DateTime.UtcNow.AddSeconds(-5)
        });
        var email = new FakeChannelAdapter(NotificationChannel.Email);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserContactInfoDto("user@example.com", null));
        var preferences = new Mock<INotificationPreferenceRepository>();
        preferences.Setup(p => p.ResolveChannelOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NotificationChannel.Email]);
        var job = new NotificationDispatchJob(sender.Object, _logs, preferences.Object, [email],
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions()), NullLogger<NotificationDispatchJob>.Instance);

        await job.ExecuteAsync(Message(inApp: false, push: false), correlation);

        email.Sends.Should().Be(1);
    }

    [Fact]
    public async Task A_stale_preference_listing_an_additive_channel_is_ignored_by_the_chain()
    {
        var email = new FakeChannelAdapter(NotificationChannel.Email);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserContactInfoDto("user@example.com", null));
        var preferences = new Mock<INotificationPreferenceRepository>();
        preferences.Setup(p => p.ResolveChannelOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NotificationChannel.InApp, NotificationChannel.Email]);
        var job = new NotificationDispatchJob(sender.Object, _logs, preferences.Object, [email],
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions()), NullLogger<NotificationDispatchJob>.Instance);

        // A full message (with an in-app payload) handed to the chain job must still only email.
        await job.ExecuteAsync(Message(push: false), Guid.NewGuid());

        email.Sends.Should().Be(1);
        _logs.Rows.Should().OnlyContain(r => r.Channel == NotificationChannel.Email);
    }

    // ----- builder ------------------------------------------------------------------------------------------------

    private sealed class CapturingEnqueuer : INotificationJobEnqueuer
    {
        public NotificationMessage? Message;
        public NotificationChannel? Forced;

        public Task<Guid> EnqueueAsync(NotificationMessage message, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
        {
            (Message, Forced) = (message, forcedChannel);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<string> ReEnqueueAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task WithPush_without_content_reuses_the_in_app_text_and_routes_by_category()
    {
        var enqueuer = new CapturingEnqueuer();

        await new NotificationSender(enqueuer).For(_user).WithPush(sensitive: true).WithInApp(InApp).SendAsync();

        var push = enqueuer.Message!.Push!;
        push.Title.Should().Be(InApp.Title);
        push.Body.Should().Be(InApp.Body);
        push.Sensitive.Should().BeTrue();
        push.Data.Should().Contain("category", "account.welcome").And.Contain("actionUrl", "/getting-started").And.Contain("step", "1");
    }

    [Fact]
    public async Task WithPush_without_content_and_without_in_app_is_a_programming_error()
    {
        await new NotificationSender(new CapturingEnqueuer()).For(_user).WithPush().Invoking(b => b.SendAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*WithInApp*");
    }

    [Fact]
    public void In_app_content_beyond_the_contract_limits_is_rejected_at_the_call_site()
    {
        var builder = new NotificationSender(new CapturingEnqueuer()).For(_user);

        builder.Invoking(b => b.WithInApp(InApp with { Category = new string('c', InAppContent.CategoryMaxLength + 1) }))
            .Should().Throw<ArgumentException>();
        builder.Invoking(b => b.WithInApp(InApp with { Category = " " })).Should().Throw<ArgumentException>();
        builder.Invoking(b => b.WithInApp(InApp with
        {
            Data = Enumerable.Range(0, InAppContent.MaxDataEntries + 1).ToDictionary(i => $"k{i}", i => "v")
        })).Should().Throw<ArgumentException>();
    }

    // ----- Hangfire argument round trip ---------------------------------------------------------------------------

    [Fact]
    public void Job_arguments_survive_Hangfire_serialization()
    {
        var inApp = new InAppDelivery(_user, NotificationCulture.Ar, InApp);
        var push = new PushDelivery(_user, NotificationCulture.En, Push with
        {
            Data = new Dictionary<string, string> { ["a"] = "b" },
            TimeToLive = TimeSpan.FromHours(2),
            Sensitive = true
        }, [Guid.NewGuid()], 3);

        var inAppBack = SerializationHelper.Deserialize<InAppDelivery>(SerializationHelper.Serialize(inApp, SerializationOption.User),
            SerializationOption.User);
        var pushBack = SerializationHelper.Deserialize<PushDelivery>(SerializationHelper.Serialize(push, SerializationOption.User),
            SerializationOption.User);

        inAppBack.Should().BeEquivalentTo(inApp);
        pushBack.Should().BeEquivalentTo(push);
    }
}
