using System.Security.Claims;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts.Devices;
using Notification.Contracts.InApp;
using Notification.Contracts.Inbox;
using Notification.Features.Devices;
using Notification.Features.Inbox;
using Shared.Contracts.Localization;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Testcontainers.PostgreSql;
using UserDirectory.Contracts.Users.IntegrationEvents;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Persistence;

/// <summary>
/// The inbox and push-device tables on real PostgreSQL through the real repositories and handlers: the raw
/// <c>ON CONFLICT</c> insert, set-based "mark all read", tenant scoping, ownership (foreign id = 404), the device
/// upsert/move/cap rules, retention and erasure.
/// </summary>
public sealed class InboxAndDevicesPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<NotificationDbContext> _options = null!;
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingRealtimeNotifier _realtime = new();
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

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

    private NotificationDbContext Db() => new(_options);

    private static IHttpContextService Caller(Guid userId, Guid? sid = null)
    {
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(userId);
        http.Setup(h => h.GetClaimValue("sid")).Returns(sid?.ToString());
        http.Setup(h => h.GetClaimValue(ClaimTypes.Sid)).Returns((string?)null);
        return http.Object;
    }

    private static ITenantContext Tenant(Guid? tenantId)
    {
        // CallBase: TenantId / IsResolved are default interface members derived from Tenant.
        var tenant = new Mock<ITenantContext> { CallBase = true };
        tenant.SetupGet(t => t.MultiTenancyEnabled).Returns(tenantId is not null);
        tenant.SetupGet(t => t.IsHostScope).Returns(false);
        tenant.SetupGet(t => t.Tenant).Returns(tenantId is { } id ? new TenantInfo(id, "t", "T") : null);
        return tenant.Object;
    }

    private async Task<InboxNotification> DeliverAsync(Guid userId, Guid? tenantId = null, string title = "Hello")
    {
        await using var db = Db();
        var job = new InAppDeliveryJob(new InboxNotificationRepository(db), new NotificationLogRepository(db), new NotificationPreferenceRepository(db, MsOptions.Create(new NotificationOptions())), _realtime,
            MsOptions.Create(new EmailOptions { AppBaseUrl = "https://app.example.com" }),
            MsOptions.Create(new InboxOptions { RetentionDays = 30 }), new NotificationMetrics(), _clock,
            NullLogger<InAppDeliveryJob>.Instance, Tenant(tenantId));
        var correlation = Guid.NewGuid();
        await job.ExecuteAsync(new InAppDelivery(userId, NotificationCulture.En,
            new InAppContent(new LocalizedText(title, title), new LocalizedText("Body", "Body"), "test.category",
                Data: new Dictionary<string, string> { ["k"] = "v" })), correlation);
        _clock.Now = _clock.Now.AddSeconds(1);
        await using var read = Db();
        return await read.InboxNotifications.AsNoTracking().SingleAsync(x => x.CorrelationId == correlation);
    }

    // ----- inbox --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_raw_insert_round_trips_every_column_and_a_duplicate_correlation_is_ignored()
    {
        var stored = await DeliverAsync(_me);
        stored.Data.Should().Contain("k", "v");
        stored.Severity.Should().Be(NotificationSeverity.Info);
        stored.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);

        await using var db = Db();
        var again = new InboxNotification
        {
            Id = Guid.NewGuid(), UserId = _me, CorrelationId = stored.CorrelationId, Category = "x", Title = "dup", Body = "dup",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        (await new InboxNotificationRepository(db).TryAddAsync(again)).Should().BeFalse();
        (await db.InboxNotifications.CountAsync(x => x.UserId == _me)).Should().Be(1);
    }

    [Fact]
    public async Task A_re_run_of_the_delivery_job_keeps_exactly_one_entry_and_one_Sent_row()
    {
        await using var db = Db();
        var job = new InAppDeliveryJob(new InboxNotificationRepository(db), new NotificationLogRepository(db), new NotificationPreferenceRepository(db, MsOptions.Create(new NotificationOptions())), _realtime,
            MsOptions.Create(new EmailOptions()), MsOptions.Create(new InboxOptions()), new NotificationMetrics(), _clock,
            NullLogger<InAppDeliveryJob>.Instance);
        var delivery = new InAppDelivery(_me, NotificationCulture.En,
            new InAppContent(new LocalizedText("a", "a"), new LocalizedText("b", "b"), "c"));
        var correlation = Guid.NewGuid();

        await job.ExecuteAsync(delivery, correlation);
        await job.ExecuteAsync(delivery, correlation);

        await using var read = Db();
        (await read.InboxNotifications.CountAsync(x => x.CorrelationId == correlation)).Should().Be(1);
        (await read.NotificationLogs.CountAsync(x => x.CorrelationId == correlation && x.Status == NotificationDeliveryStatus.Sent))
            .Should().Be(1);
    }

    [Fact]
    public async Task The_inbox_lists_only_my_entries_newest_first_and_filters_unread()
    {
        var first = await DeliverAsync(_me, title: "first");
        var second = await DeliverAsync(_me, title: "second");
        await DeliverAsync(_other, title: "not mine");

        await using var db = Db();
        var page = await new GetInboxNotificationsHandler(db, Caller(_me), Tenant(null))
            .Handle(new GetInboxNotificationsQuery { PageNumber = 1, PageSize = 10 }, default);
        page.Result!.Select(x => x.Title).Should().Equal("second", "first");

        await new MarkInboxNotificationReadHandler(new InboxNotificationRepository(db), _realtime, Caller(_me), Tenant(null), _clock)
            .Handle(new MarkInboxNotificationReadCommand(second.Id), default);
        await using var db2 = Db();
        var unread = await new GetInboxNotificationsHandler(db2, Caller(_me), Tenant(null))
            .Handle(new GetInboxNotificationsQuery { UnreadOnly = true }, default);
        unread.Result!.Should().ContainSingle(x => x.Id == first.Id);
    }

    [Fact]
    public async Task Mark_read_is_idempotent_updates_the_badge_and_tells_other_sessions()
    {
        var entry = await DeliverAsync(_me);
        await DeliverAsync(_me);
        _realtime.Counts.Clear();

        await using var db = Db();
        var handler = new MarkInboxNotificationReadHandler(new InboxNotificationRepository(db), _realtime, Caller(_me), Tenant(null), _clock);
        var first = await handler.Handle(new MarkInboxNotificationReadCommand(entry.Id), default);
        _clock.Now = _clock.Now.AddMinutes(5);
        var second = await handler.Handle(new MarkInboxNotificationReadCommand(entry.Id), default);

        first.UnreadCount.Should().Be(1);
        second.ReadAt.Should().Be(first.ReadAt, "the first read time is kept");
        _realtime.Reads.Should().ContainSingle(r => r.Read.Ids!.Contains(entry.Id));
        _realtime.Counts.Should().ContainSingle(c => c.Count == 1);
    }

    [Fact]
    public async Task Another_users_entry_is_not_found_for_read_and_delete()
    {
        var theirs = await DeliverAsync(_other);

        await using var db = Db();
        var repo = new InboxNotificationRepository(db);
        await new MarkInboxNotificationReadHandler(repo, _realtime, Caller(_me), Tenant(null), _clock)
            .Invoking(h => h.Handle(new MarkInboxNotificationReadCommand(theirs.Id), default))
            .Should().ThrowAsync<InboxNotificationNotFoundException>();
        await new DeleteInboxNotificationHandler(repo, _realtime, Caller(_me), Tenant(null))
            .Invoking(h => h.Handle(new DeleteInboxNotificationCommand(theirs.Id), default))
            .Should().ThrowAsync<InboxNotificationNotFoundException>();

        await using var read = Db();
        (await read.InboxNotifications.SingleAsync(x => x.Id == theirs.Id)).ReadAt.Should().BeNull("nothing changed");
    }

    [Fact]
    public async Task Mark_all_read_spares_entries_newer_than_upTo()
    {
        var seen = await DeliverAsync(_me, title: "seen");
        var arrivedLater = await DeliverAsync(_me, title: "arrived after the click");

        await using var db = Db();
        var result = await new MarkAllInboxNotificationsReadHandler(new InboxNotificationRepository(db), _realtime, Caller(_me),
                Tenant(null), _clock)
            .Handle(new MarkAllInboxNotificationsReadCommand(seen.CreatedAt), default);

        result.Marked.Should().Be(1);
        result.UnreadCount.Should().Be(1);
        await using var read = Db();
        (await read.InboxNotifications.SingleAsync(x => x.Id == arrivedLater.Id)).ReadAt.Should().BeNull();
        _realtime.Reads.Should().ContainSingle(r => r.Read.Ids == null && r.Read.UpTo == seen.CreatedAt);
    }

    [Fact]
    public async Task Mark_all_read_treats_an_offset_less_timestamp_as_UTC()
    {
        var entry = await DeliverAsync(_me);

        await using var db = Db();
        var result = await new MarkAllInboxNotificationsReadHandler(new InboxNotificationRepository(db), _realtime, Caller(_me),
                Tenant(null), _clock)
            .Handle(new MarkAllInboxNotificationsReadCommand(DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Unspecified)), default);

        result.Marked.Should().Be(1);
    }

    [Fact]
    public async Task With_multi_tenancy_a_tenant_sees_its_entries_plus_platform_notices_only()
    {
        Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid();
        var inA = await DeliverAsync(_me, tenantA, "A");
        await DeliverAsync(_me, tenantB, "B");
        var platform = await DeliverAsync(_me, null, "platform");
        inA.TenantId.Should().Be(tenantA, "the entry is stamped with the ambient tenant");

        await using var db = Db();
        var page = await new GetInboxNotificationsHandler(db, Caller(_me), Tenant(tenantA))
            .Handle(new GetInboxNotificationsQuery(), default);
        page.Result!.Select(x => x.Id).Should().BeEquivalentTo([inA.Id, platform.Id]);

        (await new GetUnreadInboxCountHandler(new InboxNotificationRepository(db), Caller(_me), Tenant(tenantA))
            .Handle(new GetUnreadInboxCountQuery(), default)).Count.Should().Be(2);
        (await new GetUnreadInboxCountHandler(new InboxNotificationRepository(db), Caller(_me), Tenant(null))
            .Handle(new GetUnreadInboxCountQuery(), default)).Count.Should().Be(3, "single-tenant mode sees everything");
    }

    [Fact]
    public async Task Delete_removes_the_entry_and_updates_the_badge_when_it_was_unread()
    {
        var entry = await DeliverAsync(_me);
        _realtime.Counts.Clear();

        await using var db = Db();
        var result = await new DeleteInboxNotificationHandler(new InboxNotificationRepository(db), _realtime, Caller(_me), Tenant(null))
            .Handle(new DeleteInboxNotificationCommand(entry.Id), default);

        result.UnreadCount.Should().Be(0);
        _realtime.Deleted.Should().ContainSingle(d => d.Id == entry.Id);
        _realtime.Counts.Should().ContainSingle(c => c.Count == 0);
        await using var read = Db();
        (await read.InboxNotifications.AnyAsync(x => x.Id == entry.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_entries_and_stale_devices_are_purged_and_the_rest_kept()
    {
        var keep = await DeliverAsync(_me);
        await using (var seed = Db())
        {
            seed.InboxNotifications.Add(new InboxNotification
            {
                Id = Guid.NewGuid(), UserId = _me, CorrelationId = Guid.NewGuid(), Category = "old", Title = "t", Body = "b",
                CreatedAt = _clock.Now.UtcDateTime.AddDays(-100), ExpiresAt = _clock.Now.UtcDateTime.AddDays(-1)
            });
            seed.PushDevices.AddRange(
                new PushDevice
                {
                    Id = Guid.NewGuid(), UserId = _me, Token = "stale", CreatedAt = _clock.Now.UtcDateTime.AddDays(-90),
                    LastSeenAt = _clock.Now.UtcDateTime.AddDays(-61)
                },
                new PushDevice
                {
                    Id = Guid.NewGuid(), UserId = _me, Token = "fresh", CreatedAt = _clock.Now.UtcDateTime,
                    LastSeenAt = _clock.Now.UtcDateTime
                });
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        await new NotificationDailyPurgeJob(new NotificationLogRepository(db), new InboxNotificationRepository(db),
                new PushDeviceRepository(db), MsOptions.Create(new NotificationOptions { LogPurgeBatchSize = 1 }),
                MsOptions.Create(new PushOptions { StaleDeviceDays = 60 }), _clock, NullLogger<NotificationDailyPurgeJob>.Instance)
            .ExecuteAsync();

        await using var read = Db();
        (await read.InboxNotifications.Select(x => x.Id).ToListAsync()).Should().Equal(keep.Id);
        (await read.PushDevices.Select(x => x.Token).ToListAsync()).Should().Equal("fresh");
    }

    // ----- devices ------------------------------------------------------------------------------------------------

    private async Task<RegisterPushDeviceResult> RegisterAsync(Guid userId, string token, Guid? sid = null, int max = 10)
    {
        await using var db = Db();
        var result = await new RegisterPushDeviceHandler(new PushDeviceRepository(db), Caller(userId, sid),
                MsOptions.Create(new PushOptions { MaxDevicesPerUser = max }), _clock, NullLogger<RegisterPushDeviceHandler>.Instance)
            .Handle(new RegisterPushDeviceCommand(token, PushPlatform.Android, " 1.2.3 ", "ar-EG"), default);
        _clock.Now = _clock.Now.AddMinutes(1);
        return result;
    }

    [Fact]
    public async Task Registering_is_an_upsert_bound_to_the_current_session()
    {
        var sid = Guid.NewGuid();
        var first = await RegisterAsync(_me, "fcm-token-1", sid);
        var again = await RegisterAsync(_me, "fcm-token-1", sid);

        first.Created.Should().BeTrue();
        again.Should().Be(first with { Created = false });
        await using var db = Db();
        var device = await db.PushDevices.SingleAsync();
        device.SessionId.Should().Be(sid);
        device.AppVersion.Should().Be("1.2.3");
        device.LastSeenAt.Should().BeAfter(device.CreatedAt);
    }

    [Fact]
    public async Task A_token_registered_by_another_account_moves_to_the_caller()
    {
        var mine = await RegisterAsync(_other, "shared-tablet");
        var moved = await RegisterAsync(_me, "shared-tablet");

        moved.DeviceId.Should().Be(mine.DeviceId);
        await using var db = Db();
        (await db.PushDevices.SingleAsync()).UserId.Should().Be(_me, "one install receives ONE account's pushes");
    }

    [Fact]
    public async Task The_device_cap_evicts_the_least_recently_seen()
    {
        await RegisterAsync(_me, "oldest", max: 2);
        await RegisterAsync(_me, "middle", max: 2);
        await RegisterAsync(_me, "newest", max: 2);

        await using var db = Db();
        (await db.PushDevices.Select(d => d.Token).OrderBy(t => t).ToListAsync()).Should().Equal("middle", "newest");
    }

    [Fact]
    public async Task Devices_list_masks_tokens_flags_the_current_session_and_hides_other_users()
    {
        var sid = Guid.NewGuid();
        await RegisterAsync(_me, "my-very-long-fcm-token-AAAA1111", sid);
        await RegisterAsync(_me, "my-other-fcm-token-BBBB2222");
        await RegisterAsync(_other, "their-fcm-token-CCCC3333");

        await using var db = Db();
        var list = await new GetMyPushDevicesHandler(db, Caller(_me, sid)).Handle(new GetMyPushDevicesQuery(), default);

        list.Should().HaveCount(2);
        list.Should().OnlyContain(d => d.TokenHint.StartsWith("...") && d.TokenHint.Length == 11);
        list.Single(d => d.IsCurrentSession).TokenHint.Should().Be("...AAAA1111");
    }

    [Fact]
    public async Task Unregistering_someone_elses_device_is_not_found_and_keeps_it()
    {
        var theirs = await RegisterAsync(_other, "their-token");

        await using var db = Db();
        await new UnregisterPushDeviceHandler(new PushDeviceRepository(db), Caller(_me))
            .Invoking(h => h.Handle(new UnregisterPushDeviceCommand(theirs.DeviceId), default))
            .Should().ThrowAsync<PushDeviceNotFoundException>();
        await new UnregisterPushDeviceHandler(new PushDeviceRepository(db), Caller(_other))
            .Handle(new UnregisterPushDeviceCommand(theirs.DeviceId), default);

        await using var read = Db();
        (await read.PushDevices.AnyAsync()).Should().BeFalse();
    }

    // ----- preference + erasure -----------------------------------------------------------------------------------

    [Fact]
    public async Task Push_preference_defaults_on_and_an_update_without_it_keeps_the_current_value()
    {
        await using var db = Db();
        var preferences = new NotificationPreferenceRepository(db, MsOptions.Create(new NotificationOptions()));

        (await preferences.IsPushEnabledAsync(_me)).Should().BeTrue("no row = default on");
        await preferences.UpsertAsync(_me, [NotificationChannel.Email], pushEnabled: false);
        await preferences.UpsertAsync(_me, [NotificationChannel.Email, NotificationChannel.WhatsApp]);

        (await preferences.IsPushEnabledAsync(_me)).Should().BeFalse();
    }

    [Fact]
    public async Task Erasing_a_user_deletes_their_inbox_and_devices_only()
    {
        await DeliverAsync(_me);
        await DeliverAsync(_other);
        await RegisterAsync(_me, "mine");
        await RegisterAsync(_other, "theirs");

        await using var db = Db();
        var context = new Mock<ConsumeContext<UserDeletedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserDeletedIntegrationEvent { UserId = _me });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await new UserDeletedNotificationConsumer(db, NullLogger<UserDeletedNotificationConsumer>.Instance).Consume(context.Object);

        await using var read = Db();
        (await read.InboxNotifications.AnyAsync(x => x.UserId == _me)).Should().BeFalse();
        (await read.PushDevices.AnyAsync(x => x.UserId == _me)).Should().BeFalse();
        (await read.InboxNotifications.AnyAsync(x => x.UserId == _other)).Should().BeTrue();
        (await read.PushDevices.AnyAsync(x => x.UserId == _other)).Should().BeTrue();
    }
}
