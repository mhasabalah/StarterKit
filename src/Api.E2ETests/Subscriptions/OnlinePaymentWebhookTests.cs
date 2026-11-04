using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Mediation;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Authorization;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Xunit;

namespace Api.E2ETests.Subscriptions;

/// <summary>
/// The payment webhook over real HTTP: anonymous but signature-verified, raw body, idempotent by (provider, event id),
/// and the admin list of failed events behind billing.read.
/// </summary>
[Collection(E2ECollection.Name)]
public class OnlinePaymentWebhookTests(E2ETestFixture fixture)
{
    private const string Url = "/api/v1/webhooks/payments/Fake";
    private const string FailedUrl = "/api/v1/admin/subscriptions/webhook-events/failed";

    private static string Sign(string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(E2ETestFactory.FakeGatewaySecret), Encoding.UTF8.GetBytes(body)));

    private static string Body(string eventId, string reference = "fake_does_not_exist") =>
        JsonSerializer.Serialize(new { id = eventId, type = "payment.succeeded", reference, amount = 10.00m, currency = "SAR" });

    private static HttpRequestMessage Post(string url, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (signature is not null)
            request.Headers.Add("X-Fake-Signature", signature);
        return request;
    }

    [Fact]
    public async Task Missing_or_wrong_signature_is_401()
    {
        var client = fixture.CreateAnonymousClient();
        var body = Body($"evt_{Guid.NewGuid():N}");

        (await client.SendAsync(Post(Url, body, null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.SendAsync(Post(Url, body, "deadbeef"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_garbage_is_400()
    {
        const string body = "{\"hello\":1}";

        var response = await fixture.CreateAnonymousClient().SendAsync(Post(Url, body, Sign(body)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_provider_is_404()
    {
        var body = Body($"evt_{Guid.NewGuid():N}");

        var response = await fixture.CreateAnonymousClient()
            .SendAsync(Post("/api/v1/webhooks/payments/Nope", body, Sign(body)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signed_event_is_stored_once_and_a_redelivery_is_a_200_duplicate()
    {
        var client = fixture.CreateAnonymousClient();
        var eventId = $"evt_{Guid.NewGuid():N}";
        var body = Body(eventId);

        var first = await client.SendAsync(Post(Url, body, Sign(body)));
        var second = await client.SendAsync(Post(Url, body, Sign(body)));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().Contain("\"duplicate\":false");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("\"duplicate\":true");

        // The unmatched reference is kept for an admin (failed), visible with billing.read.
        var list = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin).GetAsync($"{FailedUrl}?PageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().Contain(eventId);
    }

    [Fact]
    public async Task Failed_events_list_requires_authentication_and_billing_read()
    {
        (await fixture.CreateAnonymousClient().GetAsync(FailedUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).GetAsync(FailedUrl))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- checkout endpoint authorization / ownership -----

    private static object CheckoutBody() =>
        new { provider = "Fake", successUrl = "https://www.example.com/ok", cancelUrl = "https://www.example.com/cancel" };

    [Fact]
    public async Task Checkout_requires_authentication()
    {
        var response = await fixture.CreateAnonymousClient()
            .PostAsJsonAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/online-payment", CheckoutBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_for_another_subscribers_subscription_is_404()
    {
        var planId = await fixture.WithScopeAsync(sp => sp.GetRequiredService<ISender>().Send(new CreatePlanCommand(
            LocalizedText.Create("Online E2E", "Online E2E"), null, 100m, "SAR", 1, 10, 5, 3, IsPublic: true)));
        var owner = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        var created = await owner.PostAsJsonAsync("/api/v1/subscriptions", new { planId });
        created.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var subscriptionId = json.RootElement.GetProperty("data").GetProperty("subscriptionId").GetGuid();

        var stranger = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        var response = await stranger.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/online-payment", CheckoutBody());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
