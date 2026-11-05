using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Channels.Push;
using Notification.Contracts.Devices;
using Notification.Contracts.Enums;
using Notification.Contracts.Push;
using Notification.Dispatch;
using Notification.Persistence;
using Shared.Contracts.Localization;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Notification;

/// <summary>Runs only against a real Firebase project (set before the test run, so the host picks FCM up).</summary>
public sealed class FcmLiveE2EFactAttribute : FactAttribute
{
    public FcmLiveE2EFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("Push__Provider"), "Fcm", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Push__CredentialsFile")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FCM_TEST_TOKEN")))
            Skip = "Live FCM E2E: set Push__Provider=Fcm, Push__CredentialsFile and FCM_TEST_TOKEN.";
    }
}

/// <summary>
/// The whole push path with real Firebase: login (real JWT, session) → register the device over HTTP → the push job
/// sends through FCM (a notification appears on the device) → log out → the next push skips the device because its
/// session ended, and the device is gone.
/// </summary>
[Collection(E2ECollection.Name)]
public class FcmLiveE2ETests(E2ETestFixture fixture)
{
    private const string Password = "E2e!Passw0rd";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record LoginData(string? AccessToken, string? RefreshToken);

    [FcmLiveE2EFact]
    public async Task Login_register_push_through_FCM_then_logout_stops_the_device()
    {
        var token = Environment.GetEnvironmentVariable("FCM_TEST_TOKEN")!;
        var email = $"fcm-{Guid.NewGuid():N}@example.com";
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"fcm{Guid.NewGuid():N}", Email = email, EmailConfirmed = true, IsActive = true,
                IsProfileComplete = true
            };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User])).Succeeded.Should().BeTrue();
            return user.Id;
        });
        using var loginResponse = await fixture.CreateAnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new { identifier = email, password = Password });
        var login = (await loginResponse.Content.ReadFromJsonAsync<Envelope<LoginData>>(Json))!.Data!;
        var client = fixture.Factory.CreateClientWithToken(login.AccessToken!);

        // The host really runs FCM.
        (await fixture.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IPushProvider>().Name))).Should().Be("Fcm");

        // A previous run may have left this browser token on another test user: registering moves it.
        using (var registered = await client.PutAsJsonAsync("/api/v1/notifications/devices",
                   new RegisterPushDeviceRequest(token, PushPlatform.Web, "live-test", "en"), Json))
            registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());

        var first = Guid.NewGuid();
        await PushAsync(userId, first, "StarterKit: delivered through the API");
        (await LogsAsync(first)).Should().ContainSingle()
            .Which.Should().Match<string>(s => s.StartsWith("Sent", StringComparison.Ordinal), "FCM accepted the real device token");

        using (var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken }))
            logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = Guid.NewGuid();
        await PushAsync(userId, second, "StarterKit: you must NOT see this one");
        (await LogsAsync(second)).Should().ContainSingle()
            .Which.Should().StartWith("Skipped", "the device's session ended at logout");
        (await fixture.WithScopeAsync(sp => sp.GetRequiredService<NotificationDbContext>().PushDevices.AnyAsync(d => d.UserId == userId)))
            .Should().BeFalse("a device of an ended session is deleted");
    }

    private Task PushAsync(Guid userId, Guid correlationId, string title) =>
        fixture.WithScopeAsync(sp => sp.GetRequiredService<PushDeliveryJob>().ExecuteAsync(
            new PushDelivery(userId, NotificationCulture.En, new PushContent(
                new LocalizedText(title, title), new LocalizedText($"Live E2E at {DateTime.UtcNow:HH:mm:ss} UTC", "اختبار"),
                new Dictionary<string, string> { ["category"] = "test.live-e2e" })),
            correlationId));

    private Task<List<string>> LogsAsync(Guid correlationId) =>
        fixture.WithScopeAsync(async sp => (await sp.GetRequiredService<NotificationDbContext>().NotificationLogs
                .Where(l => l.CorrelationId == correlationId && l.Channel == NotificationChannel.Push)
                .ToListAsync())
            .Select(l => $"{l.Status}: {l.Error}").ToList());
}
