using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts.InApp;
using Shared.Contracts.Localization;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

public sealed class InAppDeliveryJobTests
{
    private readonly FakeInboxRepository _inbox = new();
    private readonly FakeNotificationLogRepository _logs = new();
    private readonly RecordingRealtimeNotifier _realtime = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
    private readonly EmailOptions _links = new() { AppBaseUrl = "https://app.example.com", AllowedLinkHosts = ["docs.example.com"] };
    private readonly Guid _user = Guid.NewGuid();

    private InAppDeliveryJob Job(IRealtimeNotifier? realtime = null) => new(
        _inbox, _logs, realtime ?? _realtime, MsOptions.Create(_links), MsOptions.Create(new InboxOptions { RetentionDays = 30 }),
        new NotificationMetrics(), _clock, NullLogger<InAppDeliveryJob>.Instance);

    private InAppDelivery Delivery(string? actionUrl = "/billing/invoices/42", NotificationCulture culture = NotificationCulture.En,
        Guid? user = null) =>
        new(user ?? _user, culture, new InAppContent(
            new LocalizedText("Invoice ready", "الفاتورة جاهزة"),
            new LocalizedText("Your invoice #42 is ready.", "فاتورتك رقم 42 جاهزة."),
            "billing.invoice-issued", actionUrl, NotificationSeverity.Success,
            new Dictionary<string, string> { ["invoiceId"] = "42" }));

    [Fact]
    public async Task Delivery_stores_the_entry_notifies_the_user_and_logs_Sent()
    {
        var correlation = Guid.NewGuid();

        await Job().ExecuteAsync(Delivery(), correlation);

        var entry = _inbox.Rows.Should().ContainSingle().Subject;
        entry.UserId.Should().Be(_user);
        entry.CorrelationId.Should().Be(correlation);
        entry.Title.Should().Be("Invoice ready");
        entry.Severity.Should().Be(NotificationSeverity.Success);
        entry.ActionUrl.Should().Be("/billing/invoices/42");
        entry.Data.Should().Contain("invoiceId", "42");
        entry.ExpiresAt.Should().Be(_clock.Now.UtcDateTime.AddDays(30));

        _realtime.Received.Should().ContainSingle(r => r.User == _user && r.Dto.Id == entry.Id);
        _realtime.Counts.Should().ContainSingle(c => c.User == _user && c.Count == 1);
        _logs.Rows.Should().ContainSingle(r => r.Channel == NotificationChannel.InApp && r.Status == NotificationDeliveryStatus.Sent
                                               && r.CorrelationId == correlation);
    }

    [Fact]
    public async Task Arabic_recipients_get_the_Arabic_text_and_an_empty_Arabic_text_falls_back_to_English()
    {
        await Job().ExecuteAsync(Delivery(culture: NotificationCulture.Ar), Guid.NewGuid());
        _inbox.Rows.Single().Title.Should().Be("الفاتورة جاهزة");

        var englishOnly = new InAppDelivery(_user, NotificationCulture.Ar, new InAppContent(
            new LocalizedText("Only English", ""), new LocalizedText("Body", " "), "x"));
        await Job().ExecuteAsync(englishOnly, Guid.NewGuid());
        _inbox.Rows.Last().Title.Should().Be("Only English");
        _inbox.Rows.Last().Body.Should().Be("Body");
    }

    [Fact]
    public async Task A_retried_job_never_creates_a_second_entry()
    {
        var correlation = Guid.NewGuid();
        var job = Job();

        await job.ExecuteAsync(Delivery(), correlation);
        await job.ExecuteAsync(Delivery(), correlation);

        _inbox.Rows.Should().ContainSingle();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent, "a finished delivery is a no-op on re-run");
    }

    [Fact]
    public async Task A_crash_after_the_insert_re_sends_the_event_for_the_stored_entry_on_retry()
    {
        var correlation = Guid.NewGuid();
        // First run: the entry was inserted, then the job died before its log row (simulated: drop the log row).
        await Job(new SilentNotifier()).ExecuteAsync(Delivery(), correlation);
        _logs.Rows.Clear();
        var stored = _inbox.Rows.Single();

        await Job().ExecuteAsync(Delivery(), correlation);

        _inbox.Rows.Should().ContainSingle();
        _realtime.Received.Should().ContainSingle().Which.Dto.Id.Should().Be(stored.Id, "clients de-duplicate by the stored id");
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task A_recipient_without_a_user_is_skipped_once()
    {
        var correlation = Guid.NewGuid();

        await Job().ExecuteAsync(Delivery(user: Guid.Empty), correlation);
        await Job().ExecuteAsync(Delivery(user: Guid.Empty), correlation);

        _inbox.Rows.Should().BeEmpty();
        _realtime.Received.Should().BeEmpty();
        _logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.test/phish")]
    [InlineData("https://evil.test/login")]
    [InlineData("https://app.example.com@evil.test/")]
    [InlineData("data:text/html,<script>")]
    [InlineData("/path with space")]
    [InlineData("/\\evil.test")]
    public async Task Unsafe_action_urls_are_dropped(string url)
    {
        await Job().ExecuteAsync(Delivery(url), Guid.NewGuid());

        _inbox.Rows.Single().ActionUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("/settings/billing", "/settings/billing")]
    [InlineData("https://app.example.com/x?y=1", "https://app.example.com/x?y=1")]
    [InlineData("https://docs.example.com/guide", "https://docs.example.com/guide")]
    public async Task Relative_and_allow_listed_action_urls_are_kept(string url, string expected)
    {
        await Job().ExecuteAsync(Delivery(url), Guid.NewGuid());

        _inbox.Rows.Single().ActionUrl.Should().Be(expected);
    }

    [Fact]
    public void Text_is_capped_and_control_characters_are_stripped()
    {
        var delivery = new InAppDelivery(_user, NotificationCulture.En, new InAppContent(
            new LocalizedText(new string('t', 500) + "\u0007", "x"), new LocalizedText("a\u0000b\nc", "x"), "cat"));

        var entry = InAppDeliveryJob.Render(delivery, Guid.NewGuid(), null, DateTime.UtcNow, _links, new InboxOptions());

        entry.Title.Should().HaveLength(InAppContent.TitleMaxLength).And.EndWith("…");
        entry.Body.Should().Be("ab\nc");
    }

    private sealed class SilentNotifier : IRealtimeNotifier
    {
        public Task NotificationReceivedAsync(Guid userId, Contracts.Inbox.InboxNotificationDto notification, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UnreadCountChangedAsync(Guid userId, Guid? tenantId, int count, CancellationToken ct = default) => Task.CompletedTask;

        public Task NotificationsReadAsync(Guid userId, Contracts.Realtime.NotificationsReadEvent read, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task NotificationDeletedAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
