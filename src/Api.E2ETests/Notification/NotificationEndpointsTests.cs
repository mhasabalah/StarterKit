using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Notification;

/// <summary>
/// The Notification module over real HTTP: admin-only endpoints refuse plain users (their routes have no
/// <c>admin</c> segment, so the route-based admin authorization test does not cover them), the delivery log is
/// readable by admins (proves the auto-migrated <c>notification</c> schema), and its enums are still served by the
/// generic enum lookup after moving to Notification.Contracts.
/// </summary>
[Collection(E2ECollection.Name)]
public class NotificationEndpointsTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/notifications";

    public static TheoryData<string, string> AdminOnlyEndpoints => new()
    {
        { "GET", $"{Base}/history" },
        { "GET", $"{Base}/preferences/{Guid.NewGuid()}" },
        { "PUT", $"{Base}/preferences/{Guid.NewGuid()}" },
        { "POST", $"{Base}/{Guid.NewGuid()}/reenqueue" },
        { "POST", $"{Base}/email/test" }
    };

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = method == "GET" ? null : JsonContent.Create(new { })
    };

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task Plain_users_are_forbidden(string method, string url)
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await user.SendAsync(Request(method, url))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task Anonymous_callers_are_unauthorized(string method, string url)
    {
        var anonymous = fixture.CreateAnonymousClient();

        (await anonymous.SendAsync(Request(method, url))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_reads_the_delivery_log_of_every_recipient()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

        var response = await admin.GetAsync($"{Base}/history?status=Failed&channel=Email&pageNumber=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_read_their_own_preferences()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        var response = await user.GetAsync($"{Base}/preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Category_groups_are_listed_with_the_mutable_channels_of_each_group()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        var response = await user.GetAsync($"{Base}/preferences/category-groups");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var groups = JsonDocument.Parse(body).RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(g => g.GetProperty("key").GetString()!);
        groups.Keys.Should().BeEquivalentTo("billing", "security", "membership", "catalog", "data-transfer", "webhooks", "system");
        groups["security"].GetProperty("inAppMutable").GetBoolean().Should().BeFalse();
        groups["security"].GetProperty("pushMutable").GetBoolean().Should().BeFalse();
        groups["security"].GetProperty("emailMutable").GetBoolean().Should().BeFalse();
        groups["billing"].GetProperty("emailMutable").GetBoolean().Should().BeFalse();
        groups["billing"].GetProperty("pushMutable").GetBoolean().Should().BeTrue();
        groups["catalog"].GetProperty("name").GetProperty("ar").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Category_groups_require_authentication()
    {
        (await fixture.CreateAnonymousClient().GetAsync($"{Base}/preferences/category-groups")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mutes_and_quiet_hours_are_stored_and_returned_and_fixed_channels_cannot_be_muted()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        foreach (var (group, mute) in new[] { ("security", "push"), ("billing", "email") })
        {
            var rejected = await user.PutAsJsonAsync($"{Base}/preferences", new
            {
                channelOrder = new[] { "Email" },
                categoryMutes = new Dictionary<string, object> { [group] = new Dictionary<string, bool> { [mute] = true } }
            });
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{group} {mute} is fixed");
        }

        var badZone = await user.PutAsJsonAsync($"{Base}/preferences", new
        {
            channelOrder = new[] { "Email" },
            quietHours = new { enabled = true, start = "22:00", end = "07:00" },
            timeZone = "Not/AZone"
        });
        badZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await user.PutAsJsonAsync($"{Base}/preferences", new
        {
            channelOrder = new[] { "Email" },
            categoryMutes = new Dictionary<string, object> { ["billing"] = new { push = true } },
            quietHours = new { enabled = true, start = "22:00", end = "07:00" },
            timeZone = "Egypt Standard Time"
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        var data = JsonDocument.Parse(await (await user.GetAsync($"{Base}/preferences")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");
        data.GetProperty("categoryMutes").GetProperty("billing").GetProperty("push").GetBoolean().Should().BeTrue();
        data.GetProperty("categoryMutes").GetProperty("billing").GetProperty("email").GetBoolean().Should().BeFalse();
        data.GetProperty("quietHours").GetProperty("start").GetString().Should().Be("22:00");
        data.GetProperty("quietHours").GetProperty("end").GetString().Should().Be("07:00");
        data.GetProperty("timeZone").GetString().Should().Be("Africa/Cairo", "a Windows id is stored as its IANA id");
    }

    [Theory]
    [InlineData("notification-channel")]
    [InlineData("notification-delivery-status")]
    [InlineData("verification-purpose")]
    [InlineData("whatsapp-template")]
    [InlineData("email-template")]
    public async Task Notification_enums_are_served_by_the_enum_lookup(string enumName)
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        var response = await user.GetAsync($"/api/v1/enums/{enumName}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
