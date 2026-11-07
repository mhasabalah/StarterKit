using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts.InApp;
using Notification.Contracts.Preferences;
using Notification.Contracts.Push;
using Notification.Features.Preferences;
using Notification.Resources;
using Shared.Contracts.Localization;
using Shared.Contracts.Mediation;
using UserDirectory.Contracts.Queries;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

/// <summary>Per-group mutes and quiet hours: the pure policy, request validation, and each delivery job's enforcement.</summary>
public sealed class NotificationPreferenceEnforcementTests
{
    private const string Cairo = "Africa/Cairo"; // UTC+2 in November (no DST)
    private static readonly TimeOnly TenPm = new(22, 0);
    private static readonly TimeOnly SevenAm = new(7, 0);

    private readonly Guid _user = Guid.NewGuid();

    private static DeliveryPreference Pref(
        Dictionary<string, ChannelMuteSet>? mutes = null, bool push = true,
        TimeOnly? start = null, TimeOnly? end = null, string? zone = null) =>
        new(push, mutes ?? new Dictionary<string, ChannelMuteSet>(), start, end, zone);

    private static DeliveryPreference Muting(string group, ChannelMuteSet set) =>
        Pref(new Dictionary<string, ChannelMuteSet> { [group] = set });

    private static DeliveryPreference Quiet() => Pref(start: TenPm, end: SevenAm, zone: Cairo);

    // ----- category groups ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("billing.invoice-issued", "billing")]
    [InlineData("Security.new-login", "security")]
    [InlineData("data-transfer.job-finished", "data-transfer")]
    [InlineData("unknown.thing", "system")]
    [InlineData("nodot", "system")]
    [InlineData("", "system")]
    [InlineData(null, "system")]
    public void GroupOf_is_the_prefix_before_the_first_dot_and_falls_back_to_system(string? category, string expected) =>
        NotificationCategoryGroups.GroupOf(category).Should().Be(expected);

    [Fact]
    public void Every_group_has_an_english_and_arabic_name_and_the_fixed_channels_are_security_and_billing_email()
    {
        NotificationCategoryGroups.All.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Name.En) && !string.IsNullOrWhiteSpace(g.Name.Ar));

        var fixedChannels = NotificationCategoryGroups.All.SelectMany(g => new[]
        {
            (g.Key, Channel: NotificationChannel.InApp, g.InAppMutable),
            (g.Key, Channel: NotificationChannel.Push, g.PushMutable),
            (g.Key, Channel: NotificationChannel.Email, g.EmailMutable)
        }).Where(x => !x.Item3).Select(x => $"{x.Key}:{x.Channel}");
        fixedChannels.Should().BeEquivalentTo("security:InApp", "security:Push", "security:Email", "billing:Email");
    }

    [Fact]
    public void Only_security_skips_quiet_hours()
    {
        NotificationCategoryGroups.All.Where(g => !NotificationCategoryGroups.DefersForQuietHours(g.Key)).Select(g => g.Key)
            .Should().Equal("security");
    }

    // ----- mute matrix ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(NotificationChannel.Push, true)]
    [InlineData(NotificationChannel.InApp, false)]
    [InlineData(NotificationChannel.Email, false)]
    public void A_mute_applies_only_to_the_muted_channel_of_that_group(NotificationChannel channel, bool muted) =>
        NotificationPolicy.IsMuted(Muting("billing", new ChannelMuteSet(Push: true)), "billing.paid", channel).Should().Be(muted);

    [Fact]
    public void A_mute_of_one_group_does_not_touch_another()
    {
        var pref = Muting("billing", new ChannelMuteSet(true, true, true));

        NotificationPolicy.IsMuted(pref, "catalog.product-published", NotificationChannel.Push).Should().BeFalse();
        NotificationPolicy.IsMuted(pref, "billing.paid", NotificationChannel.InApp).Should().BeTrue();
    }

    [Fact]
    public void An_unknown_or_dotless_category_is_governed_by_the_system_group()
    {
        var pref = Muting("system", new ChannelMuteSet(InApp: true));

        NotificationPolicy.IsMuted(pref, "weird", NotificationChannel.InApp).Should().BeTrue();
        NotificationPolicy.IsMuted(pref, "nope.thing", NotificationChannel.InApp).Should().BeTrue();
    }

    [Theory]
    [InlineData(NotificationChannel.InApp)]
    [InlineData(NotificationChannel.Push)]
    [InlineData(NotificationChannel.Email)]
    public void The_security_group_is_never_muted_even_if_a_stale_row_says_so(NotificationChannel channel) =>
        NotificationPolicy.IsMuted(Muting("security", new ChannelMuteSet(true, true, true)), "security.new-login", channel)
            .Should().BeFalse();

    [Fact]
    public void Billing_email_is_transactional_and_a_stale_mute_of_it_is_ignored_but_billing_push_and_in_app_can_be_muted()
    {
        var pref = Muting("billing", new ChannelMuteSet(true, true, true));

        NotificationPolicy.IsMuted(pref, "billing.invoice-issued", NotificationChannel.Email).Should().BeFalse();
        NotificationPolicy.IsMuted(pref, "billing.invoice-issued", NotificationChannel.Push).Should().BeTrue();
        NotificationPolicy.IsMuted(pref, "billing.invoice-issued", NotificationChannel.InApp).Should().BeTrue();
    }

    [Fact]
    public void No_preference_or_no_category_means_nothing_is_muted_on_any_channel()
    {
        var all = Muting("system", new ChannelMuteSet(true, true, true));

        NotificationPolicy.IsMuted(null, "catalog.x", NotificationChannel.Email).Should().BeFalse();
        foreach (var channel in new[] { NotificationChannel.InApp, NotificationChannel.Push, NotificationChannel.Email })
        {
            NotificationPolicy.IsMuted(all, null, channel).Should().BeFalse();
            NotificationPolicy.IsMuted(all, " ", channel).Should().BeFalse();
        }
    }

    // ----- quiet hours ----------------------------------------------------------------------------------------------

    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_crossing_midnight_is_open_before_midnight_and_ends_the_next_morning()
    {
        // 21:00Z = 23:00 in Cairo, inside 22:00-07:00; it ends at 07:00 Cairo = 05:00Z tomorrow.
        NotificationPolicy.QuietHoursEnd(Quiet(), "billing.paid", Utc(6, 21)).Should().Be(Utc(7, 5));
    }

    [Fact]
    public void A_window_crossing_midnight_is_open_after_midnight_and_ends_the_same_morning()
    {
        // 03:00Z = 05:00 in Cairo.
        NotificationPolicy.QuietHoursEnd(Quiet(), "billing.paid", Utc(7, 3)).Should().Be(Utc(7, 5));
    }

    [Theory]
    [InlineData(5, 0)] // 07:00 Cairo: the end is exclusive
    [InlineData(12, 0)]
    [InlineData(19, 59)] // 21:59 Cairo
    public void Outside_the_window_there_is_no_deferral(int hour, int minute) =>
        NotificationPolicy.QuietHoursEnd(Quiet(), "billing.paid", Utc(7, hour, minute)).Should().BeNull();

    [Fact]
    public void A_same_day_window_is_read_on_the_users_clock_not_in_UTC()
    {
        var pref = Pref(start: new TimeOnly(13, 0), end: new TimeOnly(15, 0), zone: "America/New_York"); // EST = UTC-5 in November

        NotificationPolicy.QuietHoursEnd(pref, "billing.paid", Utc(6, 19)).Should().Be(Utc(6, 20)); // 14:00 EST, ends 15:00 EST
        NotificationPolicy.QuietHoursEnd(pref, "billing.paid", Utc(6, 14)).Should().BeNull(); // 09:00 EST
    }

    [Fact]
    public void Time_zones_move_the_same_instant_in_and_out_of_the_window()
    {
        var newYork = Pref(start: TenPm, end: SevenAm, zone: "America/New_York");

        // 03:00Z = 22:00 EST (inside, ends 07:00 EST = 12:00Z) but 05:00 Cairo (also inside the Cairo window).
        NotificationPolicy.QuietHoursEnd(newYork, "billing.paid", Utc(7, 3)).Should().Be(Utc(7, 12));
        // 15:00Z = 10:00 EST (outside).
        NotificationPolicy.QuietHoursEnd(newYork, "billing.paid", Utc(7, 15)).Should().BeNull();
    }

    [Fact]
    public void Spring_forward_gap_ends_at_the_first_local_time_that_exists()
    {
        // New York 2027-03-14: 02:00 EST jumps to 03:00 EDT, so a 02:30 end does not exist and the window ends at 03:00 EDT = 07:00Z.
        var pref = Pref(start: TenPm, end: new TimeOnly(2, 30), zone: "America/New_York");
        var now = new DateTimeOffset(2027, 3, 14, 3, 0, 0, TimeSpan.Zero); // 22:00 EST on Mar 13

        NotificationPolicy.QuietHoursEnd(pref, "billing.paid", now).Should().Be(new DateTimeOffset(2027, 3, 14, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Fall_back_ambiguity_ends_at_the_first_occurrence_of_the_end_time()
    {
        // New York 2026-11-01: 02:00 EDT falls back to 01:00 EST, so 01:30 happens twice; the window ends the first time (EDT = 05:30Z).
        var pref = Pref(start: TenPm, end: new TimeOnly(1, 30), zone: "America/New_York");
        var now = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero); // 23:00 EDT on Oct 31

        NotificationPolicy.QuietHoursEnd(pref, "billing.paid", now).Should().Be(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_delivery_without_a_category_is_never_deferred()
    {
        NotificationPolicy.QuietHoursEnd(Quiet(), null, Utc(6, 21)).Should().BeNull();
        NotificationPolicy.QuietHoursEnd(Quiet(), "", Utc(6, 21)).Should().BeNull();
    }

    [Fact]
    public void Security_is_never_held_back_and_bad_data_never_blocks_delivery()
    {
        NotificationPolicy.QuietHoursEnd(Quiet(), "security.new-login", Utc(6, 21)).Should().BeNull();
        NotificationPolicy.QuietHoursEnd(Pref(start: TenPm, end: SevenAm, zone: "Mars/Olympus"), "billing.paid", Utc(6, 21)).Should().BeNull();
        NotificationPolicy.QuietHoursEnd(Pref(start: TenPm, end: TenPm, zone: Cairo), "billing.paid", Utc(6, 21)).Should().BeNull();
        NotificationPolicy.QuietHoursEnd(Pref(start: TenPm, end: SevenAm, zone: null), "billing.paid", Utc(6, 21)).Should().BeNull();
        NotificationPolicy.QuietHoursEnd(null, "billing.paid", Utc(6, 21)).Should().BeNull();
    }

    // ----- request validation ---------------------------------------------------------------------------------------

    private static IStringLocalizer<NotificationResource> Localizer()
    {
        var localizer = new Mock<IStringLocalizer<NotificationResource>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns((string k) => new LocalizedString(k, k));
        localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string k, object[] _) => new LocalizedString(k, k));
        return localizer.Object;
    }

    private static PreferenceChange? Build(UpdatePreferenceRequest request, DeliveryPreference? existing, out string? error) =>
        PreferenceRules.Build(request, existing, [new FakeChannelAdapter(NotificationChannel.Email)], Localizer(), out error);

    private static UpdatePreferenceRequest Request(
        Dictionary<string, ChannelMuteSet>? mutes = null, QuietHoursDto? quiet = null, string? zone = null) =>
        new([NotificationChannel.Email], null, mutes, quiet, zone);

    [Fact]
    public void A_valid_update_keeps_normalized_mutes_and_drops_empty_entries()
    {
        var change = Build(Request(new Dictionary<string, ChannelMuteSet>
        {
            ["Billing"] = new(Push: true),
            ["catalog"] = new()
        }), null, out var error);

        error.Should().BeNull();
        change!.CategoryMutes.Should().ContainSingle().Which.Key.Should().Be("billing");
    }

    [Fact]
    public void An_unknown_group_is_rejected()
    {
        Build(Request(new Dictionary<string, ChannelMuteSet> { ["nonsense"] = new(Push: true) }), null, out var error).Should().BeNull();
        error.Should().Be("Validation.Preference.UnknownGroup");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Muting_security_on_any_channel_is_rejected(bool inApp, bool push, bool email)
    {
        Build(Request(new Dictionary<string, ChannelMuteSet> { ["security"] = new(inApp, push, email) }), null, out var error)
            .Should().BeNull();
        error.Should().Be("Validation.Preference.ChannelNotMutable");
    }

    [Fact]
    public void Muting_billing_email_is_rejected_but_billing_push_and_in_app_are_accepted()
    {
        Build(Request(new Dictionary<string, ChannelMuteSet> { ["billing"] = new(Email: true) }), null, out var error).Should().BeNull();
        error.Should().Be("Validation.Preference.ChannelNotMutable");

        Build(Request(new Dictionary<string, ChannelMuteSet> { ["billing"] = new(InApp: true, Push: true) }), null, out error).Should().NotBeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void The_stored_time_zone_is_the_canonical_IANA_id_and_windows_ids_are_converted()
    {
        PreferenceRules.CanonicalIanaId("Africa/Cairo").Should().Be("Africa/Cairo");
        PreferenceRules.CanonicalIanaId("UTC").Should().NotBeNull();
        PreferenceRules.CanonicalIanaId("Egypt Standard Time").Should().Be("Africa/Cairo");
        PreferenceRules.CanonicalIanaId("Not/AZone").Should().BeNull();
        PreferenceRules.CanonicalIanaId(new string('x', 80)).Should().BeNull();

        var change = Build(Request(quiet: new QuietHoursDto(true, "22:00", "07:00"), zone: "Egypt Standard Time"), null, out var error);
        error.Should().BeNull();
        change!.TimeZone.Should().Be("Africa/Cairo");
    }

    [Fact]
    public void An_all_false_security_entry_is_harmless_and_dropped()
    {
        var change = Build(Request(new Dictionary<string, ChannelMuteSet> { ["security"] = new() }), null, out var error);

        error.Should().BeNull();
        change!.CategoryMutes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("22:00", "07:00", "Africa/Cairo", null)]
    [InlineData("22:00:00", "07:30:00", "UTC", null)]
    [InlineData("22:00", "07:00", null, "Africa/Cairo")] // the zone was stored earlier
    public void A_quiet_window_with_a_known_time_zone_is_accepted(string start, string end, string? zone, string? stored)
    {
        var existing = stored is null ? null : Pref(zone: stored);

        var change = Build(Request(quiet: new QuietHoursDto(true, start, end), zone: zone), existing, out var error);

        error.Should().BeNull();
        change!.QuietHoursStart.Should().NotBeNull();
        change.QuietHoursEnd.Should().NotBeNull();
    }

    [Theory]
    [InlineData("25:00", "07:00", "Africa/Cairo", "Validation.Preference.QuietHours.Invalid")]
    [InlineData("22:00", null, "Africa/Cairo", "Validation.Preference.QuietHours.Invalid")]
    [InlineData("08:00", "08:00", "Africa/Cairo", "Validation.Preference.QuietHours.SameTime")]
    [InlineData("22:00", "07:00", null, "Validation.Preference.TimeZone.Required")]
    [InlineData("22:00", "07:00", "Not/AZone", "Validation.Preference.TimeZone.Invalid")]
    public void An_invalid_quiet_window_is_rejected(string? start, string? end, string? zone, string expected)
    {
        Build(Request(quiet: new QuietHoursDto(true, start, end), zone: zone), null, out var error).Should().BeNull();
        error.Should().Be(expected);
    }

    [Fact]
    public void Disabling_quiet_hours_clears_the_window()
    {
        var change = Build(Request(quiet: new QuietHoursDto(false)), Quiet(), out var error);

        error.Should().BeNull();
        change!.ClearQuietHours.Should().BeTrue();
    }

    // ----- push job -------------------------------------------------------------------------------------------------

    private sealed class PushHarness
    {
        public readonly FakePushDeviceRepository Devices = new();
        public readonly FakeNotificationLogRepository Logs = new();
        public readonly FakePushProvider Provider = new();
        public readonly Mock<IBackgroundJobClient> Jobs = new();
        public readonly Mock<INotificationPreferenceRepository> Preferences = new();
        public readonly List<(Job Job, IState State)> Scheduled = [];
        public readonly ManualTimeProvider Clock;
        public readonly PushOptions Options = new();

        public PushHarness(DateTimeOffset now, DeliveryPreference? preference)
        {
            Clock = new ManualTimeProvider(now);
            Preferences.Setup(p => p.GetDeliveryPreferenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(preference);
            Jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Callback<Job, IState>((job, state) => Scheduled.Add((job, state))).Returns("job-1");
        }

        public PushDeliveryJob Job() => new(Mock.Of<ISender>(), Devices, Preferences.Object, Logs, Provider, Jobs.Object,
            MsOptions.Create(Options), new NotificationMetrics(), Clock, NullLogger<PushDeliveryJob>.Instance);
    }

    private PushDelivery PushFor(string category, TimeSpan? ttl = null) =>
        new(_user, NotificationCulture.En, new PushContent(new LocalizedText("T", "ت"), new LocalizedText("B", "ب"),
            new Dictionary<string, string> { ["category"] = category }, TimeToLive: ttl));

    [Fact]
    public async Task A_muted_push_group_sends_nothing_and_logs_Skipped()
    {
        var h = new PushHarness(Utc(7, 12), Muting("billing", new ChannelMuteSet(Push: true)));
        h.Devices.Add(_user);

        await h.Job().ExecuteAsync(PushFor("billing.paid"), Guid.NewGuid());

        h.Provider.Calls.Should().BeEmpty();
        h.Logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped && r.Error!.Contains("billing"));
    }

    [Fact]
    public async Task Muting_billing_push_does_not_stop_a_catalog_push()
    {
        var h = new PushHarness(Utc(7, 12), Muting("billing", new ChannelMuteSet(Push: true)));
        h.Devices.Add(_user);

        await h.Job().ExecuteAsync(PushFor("catalog.product-published"), Guid.NewGuid());

        h.Provider.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Quiet_hours_defer_the_push_to_the_end_of_the_window_with_the_remaining_time_to_live()
    {
        var h = new PushHarness(Utc(6, 21), Quiet()); // 23:00 Cairo; the window ends in 8 hours
        h.Devices.Add(_user);
        var correlation = Guid.NewGuid();

        await h.Job().ExecuteAsync(PushFor("billing.paid", TimeSpan.FromHours(20)), correlation);

        h.Provider.Calls.Should().BeEmpty();
        var (job, state) = h.Scheduled.Should().ContainSingle().Subject;
        // Hangfire stamps the schedule with the real clock, so assert the delay (8 hours until 07:00 Cairo).
        (((ScheduledState)state).EnqueueAt - DateTime.UtcNow).Should().BeCloseTo(TimeSpan.FromHours(8), TimeSpan.FromMinutes(1));
        job.Type.Should().Be<PushDeliveryJob>();
        var deferred = (PushDelivery)job.Args[0];
        deferred.Content.TimeToLive.Should().Be(TimeSpan.FromHours(12));
        job.Args[1].Should().Be(correlation);
    }

    [Fact]
    public async Task Deferral_writes_a_log_row_and_a_duplicate_run_of_the_original_job_schedules_nothing_more()
    {
        var h = new PushHarness(Utc(6, 21), Quiet());
        h.Devices.Add(_user);
        var correlation = Guid.NewGuid();

        await h.Job().ExecuteAsync(PushFor("billing.paid"), correlation);
        await h.Job().ExecuteAsync(PushFor("billing.paid"), correlation);

        h.Scheduled.Should().ContainSingle();
        h.Logs.Rows.Should().ContainSingle(r => r.Channel == NotificationChannel.Push && r.Status == NotificationDeliveryStatus.Queued
                                                && r.Error!.StartsWith("Deferred until "));
    }

    [Fact]
    public async Task A_deferred_run_that_fires_early_reschedules_without_a_second_log_row()
    {
        var h = new PushHarness(Utc(6, 21), Quiet());
        h.Devices.Add(_user);
        var correlation = Guid.NewGuid();
        await h.Job().ExecuteAsync(PushFor("billing.paid"), correlation);
        var deferred = (PushDelivery)h.Scheduled.Single().Job.Args[0];

        await h.Job().ExecuteAsync(deferred, correlation); // still inside the window

        h.Scheduled.Should().HaveCount(2);
        h.Logs.Rows.Where(r => r.Status == NotificationDeliveryStatus.Queued).Should().ContainSingle();
        h.Provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_push_without_a_category_is_never_muted_or_deferred()
    {
        var h = new PushHarness(Utc(6, 21), Pref(new Dictionary<string, ChannelMuteSet> { ["system"] = new(Push: true) }, start: TenPm, end: SevenAm, zone: Cairo));
        h.Devices.Add(_user);
        var delivery = new PushDelivery(_user, NotificationCulture.En, new PushContent(new LocalizedText("T", "ت"), new LocalizedText("B", "ب")));

        await h.Job().ExecuteAsync(delivery, Guid.NewGuid());

        h.Provider.Calls.Should().ContainSingle();
        h.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task The_deferred_push_goes_out_once_the_window_has_ended()
    {
        var h = new PushHarness(Utc(7, 5), Quiet()); // 07:00 Cairo
        h.Devices.Add(_user);

        await h.Job().ExecuteAsync(PushFor("billing.paid"), Guid.NewGuid());

        h.Provider.Calls.Should().ContainSingle();
        h.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_push_whose_time_to_live_ends_inside_the_window_is_dropped_not_deferred()
    {
        var h = new PushHarness(Utc(6, 21), Quiet());
        h.Devices.Add(_user);

        await h.Job().ExecuteAsync(PushFor("billing.paid", TimeSpan.FromHours(2)), Guid.NewGuid());

        h.Provider.Calls.Should().BeEmpty();
        h.Scheduled.Should().BeEmpty();
        h.Logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Skipped && r.Error!.Contains("time-to-live"));
    }

    [Fact]
    public async Task Security_push_ignores_both_the_mute_and_quiet_hours()
    {
        var pref = Pref(new Dictionary<string, ChannelMuteSet> { ["security"] = new(Push: true) }, start: TenPm, end: SevenAm, zone: Cairo);
        var h = new PushHarness(Utc(6, 21), pref);
        h.Devices.Add(_user);

        await h.Job().ExecuteAsync(PushFor("security.new-login"), Guid.NewGuid());

        h.Provider.Calls.Should().ContainSingle();
        h.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task The_category_passed_by_the_sender_is_used_when_the_content_carries_none()
    {
        var h = new PushHarness(Utc(7, 12), Muting("billing", new ChannelMuteSet(Push: true)));
        h.Devices.Add(_user);
        var delivery = new PushDelivery(_user, NotificationCulture.En,
            new PushContent(new LocalizedText("T", "ت"), new LocalizedText("B", "ب")), Category: "billing.paid");

        await h.Job().ExecuteAsync(delivery, Guid.NewGuid());

        h.Provider.Calls.Should().BeEmpty();
    }

    // ----- in-app job -----------------------------------------------------------------------------------------------

    private sealed class InAppHarness
    {
        public readonly FakeInboxRepository Inbox = new();
        public readonly FakeNotificationLogRepository Logs = new();
        public readonly RecordingRealtimeNotifier Realtime = new();
        public readonly ManualTimeProvider Clock;
        private readonly Mock<INotificationPreferenceRepository> _preferences = new();

        public InAppHarness(DateTimeOffset now, DeliveryPreference? preference)
        {
            Clock = new ManualTimeProvider(now);
            _preferences.Setup(p => p.GetDeliveryPreferenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(preference);
        }

        public InAppDeliveryJob Job() => new(Inbox, Logs, _preferences.Object, Realtime,
            MsOptions.Create(new EmailOptions { AppBaseUrl = "https://app.example.com" }),
            MsOptions.Create(new InboxOptions { RetentionDays = 30 }), new NotificationMetrics(), Clock,
            NullLogger<InAppDeliveryJob>.Instance);
    }

    private InAppDelivery InAppFor(string category) =>
        new(_user, NotificationCulture.En, new InAppContent(new LocalizedText("T", "ت"), new LocalizedText("B", "ب"), category));

    [Fact]
    public async Task A_muted_in_app_group_stores_nothing_and_logs_Skipped()
    {
        var h = new InAppHarness(Utc(7, 12), Muting("billing", new ChannelMuteSet(InApp: true)));

        await h.Job().ExecuteAsync(InAppFor("billing.paid"), Guid.NewGuid());

        h.Inbox.Rows.Should().BeEmpty();
        h.Realtime.Received.Should().BeEmpty();
        h.Logs.Rows.Should().ContainSingle(r => r.Channel == NotificationChannel.InApp && r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task The_in_app_category_resolved_by_the_sender_wins_over_the_content_category()
    {
        var h = new InAppHarness(Utc(7, 12), Muting("billing", new ChannelMuteSet(InApp: true)));
        var delivery = InAppFor("catalog.something") with { Category = "billing.paid" };

        await h.Job().ExecuteAsync(delivery, Guid.NewGuid());

        h.Inbox.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task During_quiet_hours_the_entry_is_stored_and_counted_but_not_pushed_live()
    {
        var h = new InAppHarness(Utc(6, 21), Quiet());

        await h.Job().ExecuteAsync(InAppFor("billing.paid"), Guid.NewGuid());

        h.Inbox.Rows.Should().ContainSingle();
        h.Realtime.Received.Should().BeEmpty("no live pop-up while the user is in quiet hours");
        h.Realtime.Counts.Should().ContainSingle(c => c.User == _user && c.Count == 1, "the unread badge stays consistent");
        h.Logs.Rows.Should().ContainSingle(r => r.Status == NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task Outside_quiet_hours_and_for_security_the_entry_is_pushed_live()
    {
        var outside = new InAppHarness(Utc(7, 12), Quiet());
        await outside.Job().ExecuteAsync(InAppFor("billing.paid"), Guid.NewGuid());
        outside.Realtime.Received.Should().ContainSingle();

        var security = new InAppHarness(Utc(6, 21), Quiet());
        await security.Job().ExecuteAsync(InAppFor("security.new-login"), Guid.NewGuid());
        security.Realtime.Received.Should().ContainSingle();
    }

    // ----- email chain ----------------------------------------------------------------------------------------------

    private (NotificationDispatchJob Job, FakeChannelAdapter Email, FakeNotificationLogRepository Logs) EmailJob(DeliveryPreference? preference)
    {
        var email = new FakeChannelAdapter(NotificationChannel.Email);
        var logs = new FakeNotificationLogRepository();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserContactInfoDto("user@example.com", null));
        var preferences = new Mock<INotificationPreferenceRepository>();
        preferences.Setup(p => p.ResolveChannelOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NotificationChannel.Email]);
        preferences.Setup(p => p.GetDeliveryPreferenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(preference);
        var job = new NotificationDispatchJob(sender.Object, logs, preferences.Object, [email],
            MsOptions.Create(new NotificationOptions()), NullLogger<NotificationDispatchJob>.Instance);
        return (job, email, logs);
    }

    private NotificationMessage EmailMessage(string? category) => new()
    {
        UserId = _user,
        Category = category,
        Email = new EmailPayload(EmailTemplate.WelcomeUser, new Dictionary<string, object>())
    };

    [Fact]
    public async Task A_muted_email_group_is_skipped_and_logged()
    {
        var (job, email, logs) = EmailJob(Muting("membership", new ChannelMuteSet(Email: true)));

        await job.ExecuteAsync(EmailMessage("membership.invitation-accepted"), Guid.NewGuid());

        email.Sends.Should().Be(0);
        logs.Rows.Should().ContainSingle(r => r.Channel == NotificationChannel.Email && r.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task A_rerun_of_a_muted_email_adds_no_second_Skipped_row()
    {
        var (job, _, logs) = EmailJob(Muting("membership", new ChannelMuteSet(Email: true)));
        var correlation = Guid.NewGuid();

        await job.ExecuteAsync(EmailMessage("membership.invitation-accepted"), correlation);
        await job.ExecuteAsync(EmailMessage("membership.invitation-accepted"), correlation);

        logs.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Email_is_sent_for_security_billing_uncategorized_and_unmuted_groups_even_with_stale_mutes()
    {
        var muted = Muting("membership", new ChannelMuteSet(Email: true));

        var security = EmailJob(Muting("security", new ChannelMuteSet(Email: true)));
        await security.Job.ExecuteAsync(EmailMessage("security.password-changed"), Guid.NewGuid());
        security.Email.Sends.Should().Be(1);

        var billing = EmailJob(Muting("billing", new ChannelMuteSet(Email: true)));
        await billing.Job.ExecuteAsync(EmailMessage("billing.invoice-issued"), Guid.NewGuid());
        billing.Email.Sends.Should().Be(1, "billing email is transactional");

        var none = EmailJob(muted);
        await none.Job.ExecuteAsync(EmailMessage(null), Guid.NewGuid());
        none.Email.Sends.Should().Be(1, "an email-only send without a category is never muted");

        var uncategorizedForced = EmailJob(Muting("system", new ChannelMuteSet(Email: true)));
        await uncategorizedForced.Job.ExecuteAsync(EmailMessage(null), Guid.NewGuid(), NotificationChannel.Email);
        uncategorizedForced.Email.Sends.Should().Be(1, "forced email without a category is never muted");

        var other = EmailJob(muted);
        await other.Job.ExecuteAsync(EmailMessage("catalog.product-published"), Guid.NewGuid());
        other.Email.Sends.Should().Be(1);
    }

    [Fact]
    public async Task Muting_email_mutes_the_whole_chain_and_never_falls_back_to_WhatsApp()
    {
        var email = new FakeChannelAdapter(NotificationChannel.Email);
        var whatsApp = new FakeChannelAdapter(NotificationChannel.WhatsApp);
        var logs = new FakeNotificationLogRepository();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserContactInfoDto("user@example.com", "+201001234567"));
        var preferences = new Mock<INotificationPreferenceRepository>();
        preferences.Setup(p => p.ResolveChannelOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NotificationChannel.Email, NotificationChannel.WhatsApp]);
        preferences.Setup(p => p.GetDeliveryPreferenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Muting("membership", new ChannelMuteSet(Email: true)));
        var job = new NotificationDispatchJob(sender.Object, logs, preferences.Object, [email, whatsApp],
            MsOptions.Create(new NotificationOptions()), NullLogger<NotificationDispatchJob>.Instance);
        var message = EmailMessage("membership.invitation-accepted") with { WhatsApp = new WhatsAppPayload(WhatsAppTemplate.SubscriptionActivated) };

        await job.ExecuteAsync(message, Guid.NewGuid());

        email.Sends.Should().Be(0);
        whatsApp.Sends.Should().Be(0);
    }

    // ----- builder --------------------------------------------------------------------------------------------------

    private static async Task<NotificationMessage> BuildAsync(Action<INotificationBuilder> configure)
    {
        NotificationMessage? captured = null;
        var enqueuer = new Mock<INotificationJobEnqueuer>();
        enqueuer.Setup(e => e.EnqueueAsync(It.IsAny<NotificationMessage>(), It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationMessage, NotificationChannel?, CancellationToken>((m, _, _) => captured = m)
            .ReturnsAsync(Guid.NewGuid());
        INotificationBuilder builder = new NotificationBuilder(Guid.NewGuid(), NotificationCulture.En, enqueuer.Object);
        configure(builder);
        await builder.SendAsync();
        return captured!;
    }

    [Fact]
    public async Task InCategory_sets_the_category_of_an_email_only_send()
    {
        var message = await BuildAsync(b => b.WithEmail(EmailTemplate.WelcomeUser).InCategory("billing.invoice-issued"));

        message.Category.Should().Be("billing.invoice-issued");
    }

    [Fact]
    public async Task The_category_defaults_to_the_in_app_category_and_InCategory_wins_over_it()
    {
        var inApp = new InAppContent(new LocalizedText("T", "ت"), new LocalizedText("B", "ب"), "catalog.product-published");

        (await BuildAsync(b => b.WithEmail(EmailTemplate.WelcomeUser).WithInApp(inApp))).Category.Should().Be("catalog.product-published");
        (await BuildAsync(b => b.WithInApp(inApp).InCategory("billing.paid"))).Category.Should().Be("billing.paid");
        (await BuildAsync(b => b.WithEmail(EmailTemplate.WelcomeUser))).Category.Should().BeNull();
    }
}
