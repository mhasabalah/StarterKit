using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Contracts.Devices;
using Notification.Contracts.InApp;
using Notification.Contracts.Inbox;
using Notification.Contracts.Realtime;
using Notification.Dispatch;
using Shared.Contracts.Localization;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Notification;

/// <summary>
/// The in-app inbox, push devices and the SignalR hub through the REAL host: real JWT login, the real authentication
/// pipeline (query-string token on /hubs only, blacklist, session revocation), real Postgres, real handlers.
/// </summary>
[Collection(E2ECollection.Name)]
public class InboxAndRealtimeE2ETests(E2ETestFixture fixture)
{
    private const string Inbox = "/api/v1/notifications/inbox";
    private const string Devices = "/api/v1/notifications/devices";
    private const string Password = "E2e!Passw0rd";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record LoginData(string? AccessToken, string? RefreshToken);

    private sealed record Page<T>(List<T>? Result);

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> SignInAsync()
    {
        var email = $"inbox-{Guid.NewGuid():N}@example.com";
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"inbox{Guid.NewGuid():N}", Email = email, EmailConfirmed = true, IsActive = true,
                IsProfileComplete = true
            };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User])).Succeeded.Should().BeTrue();
            return user.Id;
        });

        using var response = await fixture.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new { identifier = email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var login = (await response.Content.ReadFromJsonAsync<Envelope<LoginData>>(Json))!.Data!;
        return (userId, login.AccessToken!, login.RefreshToken!);
    }

    /// <summary>A browser-style connection: WebSockets, no negotiate, token in the query string (accessTokenFactory).</summary>
    private HubConnection Hub(string? accessToken)
    {
        var server = fixture.Factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, NotificationHubContract.Path.TrimStart('/')), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
                o.AccessTokenProvider = () => Task.FromResult(accessToken);
                o.WebSocketFactory = async (context, ct) =>
                {
                    // The .NET client puts the token in the URL for WebSockets exactly like the JS client does.
                    var uri = accessToken is null ? context.Uri : new UriBuilder(context.Uri)
                    {
                        Query = (context.Uri.Query.TrimStart('?') + "&access_token=" + Uri.EscapeDataString(accessToken)).TrimStart('&')
                    }.Uri;
                    return await server.CreateWebSocketClient().ConnectAsync(uri, ct);
                };
            })
            .Build();
    }

    private async Task DeliverAsync(Guid userId, string title) =>
        await fixture.WithScopeAsync(async sp =>
        {
            var job = sp.GetRequiredService<InAppDeliveryJob>();
            await job.ExecuteAsync(new InAppDelivery(userId, global::Notification.Contracts.Enums.NotificationCulture.En,
                new InAppContent(new LocalizedText(title, title), new LocalizedText("body", "body"), "e2e.test", "/somewhere")),
                Guid.NewGuid());
        });

    // ----- hub ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_signed_in_user_connects_with_the_query_string_token_and_receives_their_notifications()
    {
        var (userId, token, _) = await SignInAsync();
        var received = new TaskCompletionSource<InboxNotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = new TaskCompletionSource<UnreadCountChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = Hub(token);
        hub.On<InboxNotificationDto>(NotificationHubContract.NotificationReceived, n => received.TrySetResult(n));
        hub.On<UnreadCountChangedEvent>(NotificationHubContract.UnreadCountChanged, c => count.TrySetResult(c));
        await hub.StartAsync();

        await DeliverAsync(userId, "Real-time hello");

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(15))).Title.Should().Be("Real-time hello");
        (await count.Task.WaitAsync(TimeSpan.FromSeconds(15))).Count.Should().Be(1);
    }

    [Fact]
    public async Task Without_a_token_the_hub_refuses_the_connection()
    {
        await using var hub = Hub(accessToken: null);

        await hub.Invoking(h => h.StartAsync()).Should().ThrowAsync<Exception>();
        hub.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task A_logged_out_token_can_no_longer_open_the_hub_even_from_the_query_string()
    {
        var (_, token, refresh) = await SignInAsync();
        await using (var before = Hub(token))
        {
            await before.StartAsync();
            before.State.Should().Be(HubConnectionState.Connected);
        }

        using var logout = await fixture.Factory.CreateClientWithToken(token)
            .PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh, logoutAllDevices = false });
        logout.StatusCode.Should().Be(HttpStatusCode.OK, await logout.Content.ReadAsStringAsync());

        await using var after = Hub(token);
        await after.Invoking(h => h.StartAsync()).Should().ThrowAsync<Exception>(
            "the blacklist check runs on the validated token, not only on the Authorization header");
    }

    [Fact]
    public async Task A_query_string_token_is_ignored_outside_the_hub_routes()
    {
        var (_, token, _) = await SignInAsync();

        using var response = await fixture.CreateAnonymousClient().GetAsync($"{Inbox}/unread-count?access_token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ----- inbox REST ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Inbox_list_count_read_read_all_and_delete_over_http()
    {
        var (userId, token, _) = await SignInAsync();
        var client = fixture.Factory.CreateClientWithToken(token);
        await DeliverAsync(userId, "one");
        await DeliverAsync(userId, "two");
        await DeliverAsync(userId, "three");

        var page = await client.GetFromJsonAsync<Envelope<Page<InboxNotificationDto>>>($"{Inbox}?pageNumber=1&pageSize=2", Json);
        page!.Data!.Result!.Select(x => x.Title).Should().Equal("three", "two");
        (await client.GetFromJsonAsync<Envelope<GetUnreadInboxCountResult>>($"{Inbox}/unread-count", Json))!.Data!.Count.Should().Be(3);

        var newest = page.Data.Result![0];
        using (var read = await client.PostAsync($"{Inbox}/{newest.Id}/read", null))
        {
            read.StatusCode.Should().Be(HttpStatusCode.OK);
            (await read.Content.ReadFromJsonAsync<Envelope<MarkInboxNotificationReadResult>>(Json))!.Data!.UnreadCount.Should().Be(2);
        }

        using (var del = await client.DeleteAsync($"{Inbox}/{newest.Id}"))
            del.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var all = await client.PostAsJsonAsync($"{Inbox}/read-all", new { }))
            (await all.Content.ReadFromJsonAsync<Envelope<MarkAllInboxNotificationsReadResult>>(Json))!.Data!
                .Should().Be(new MarkAllInboxNotificationsReadResult(2, 0));
    }

    [Fact]
    public async Task Another_users_entry_is_404_and_anonymous_is_401()
    {
        var (owner, _, _) = await SignInAsync();
        await DeliverAsync(owner, "private");
        var entryId = await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<global::Notification.Persistence.NotificationDbContext>();
            return await db.InboxNotifications.Where(x => x.UserId == owner).Select(x => x.Id).SingleAsync();
        });
        var stranger = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await stranger.PostAsync($"{Inbox}/{entryId}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"{Inbox}/{entryId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.CreateAnonymousClient().GetAsync(Inbox)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_invalid_page_size_is_a_validation_error()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await user.GetAsync($"{Inbox}?pageSize=1000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ----- devices + preferences ----------------------------------------------------------------------------------

    [Fact]
    public async Task Devices_register_list_masked_and_unregister()
    {
        var (_, token, _) = await SignInAsync();
        var client = fixture.Factory.CreateClientWithToken(token);
        const string fcmToken = "fcm-e2e-token-0123456789-abcdefgh";

        using var registered = await client.PutAsJsonAsync(Devices,
            new RegisterPushDeviceRequest(fcmToken, global::Notification.Contracts.Enums.PushPlatform.Android, "1.0.0", "en"), Json);
        registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
        var device = (await registered.Content.ReadFromJsonAsync<Envelope<RegisterPushDeviceResult>>(Json))!.Data!;
        device.Created.Should().BeTrue();

        var list = (await client.GetFromJsonAsync<Envelope<List<PushDeviceDto>>>(Devices, Json))!.Data!;
        list.Should().ContainSingle(d => d.Id == device.DeviceId && d.TokenHint == "...abcdefgh" && d.IsCurrentSession);
        (await client.GetStringAsync(Devices)).Should().NotContain(fcmToken, "the full token is never returned");

        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).DeleteAsync($"{Devices}/{device.DeviceId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Devices}/{device.DeviceId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<Envelope<List<PushDeviceDto>>>(Devices, Json))!.Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has whitespace")]
    public async Task An_invalid_device_token_is_a_validation_error(string fcmToken)
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await user.PutAsJsonAsync(Devices, new { token = fcmToken, platform = "Android" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_can_be_turned_off_and_in_app_or_push_cannot_join_the_channel_order()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        using var off = await user.PutAsJsonAsync("/api/v1/notifications/preferences", new { channelOrder = new[] { "Email" }, pushEnabled = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        (await user.GetStringAsync("/api/v1/notifications/preferences")).Should().Contain("\"pushEnabled\":false");

        (await user.PutAsJsonAsync("/api/v1/notifications/preferences", new { channelOrder = new[] { "InApp", "Email" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("notification-severity")]
    [InlineData("push-platform")]
    public async Task The_new_enums_are_served_by_the_enum_lookup(string enumName)
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await user.GetAsync($"/api/v1/enums/{enumName}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
