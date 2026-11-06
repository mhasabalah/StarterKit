using System.Net;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Subscriptions.Data;
using Subscriptions.Domains.OnlinePayments.Models;
using Xunit;

namespace Api.E2ETests.Subscriptions;

/// <summary>The saved payment method endpoints over real HTTP: authentication, ownership and the single default.</summary>
[Collection(E2ECollection.Name)]
public class SavedPaymentMethodsTests(E2ETestFixture fixture)
{
    private const string Url = "/api/v1/subscriptions/saved-payment-methods";

    private async Task<Guid> SeedAsync(Guid subscriberId, string methodRef, bool isDefault)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var method = SavedPaymentMethod.Create(
            subscriberId, "Fake", "cus_e2e", methodRef, "visa", "4242", 12, 2099, isDefault, DateTimeOffset.UtcNow);
        db.SavedPaymentMethods.Add(method);
        await db.SaveChangesAsync();
        return method.Id;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Every_endpoint_requires_authentication()
    {
        var client = fixture.CreateAnonymousClient();
        var id = Guid.NewGuid();

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"{Url}/{id}/default", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_returns_only_the_callers_active_methods_without_provider_references()
    {
        var me = Guid.NewGuid();
        await SeedAsync(me, $"pm_{Guid.NewGuid():N}", isDefault: true);
        await SeedAsync(Guid.NewGuid(), $"pm_{Guid.NewGuid():N}", isDefault: true);

        var response = await fixture.CreateClientAs(me, RoleConstants.User).GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("last4").GetString().Should().Be("4242");
        data[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
        data[0].TryGetProperty("providerMethodRef", out _).Should().BeFalse();
        data[0].TryGetProperty("providerCustomerRef", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Another_subscribers_method_is_reported_as_missing()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, $"pm_{Guid.NewGuid():N}", isDefault: true);
        var intruder = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await intruder.PostAsync($"{Url}/{id}/default", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.CreateClientAs(owner, RoleConstants.User).GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Setting_a_default_moves_it_and_removing_the_default_promotes_the_other()
    {
        var me = Guid.NewGuid();
        var first = await SeedAsync(me, $"pm_{Guid.NewGuid():N}", isDefault: true);
        var second = await SeedAsync(me, $"pm_{Guid.NewGuid():N}", isDefault: false);
        var client = fixture.CreateClientAs(me, RoleConstants.User);

        (await client.PostAsync($"{Url}/{second}/default", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterSwitch = await DataAsync(await client.GetAsync(Url));
        afterSwitch.EnumerateArray().Single(x => x.GetProperty("isDefault").GetBoolean())
            .GetProperty("id").GetGuid().Should().Be(second);

        (await client.DeleteAsync($"{Url}/{second}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterRemove = await DataAsync(await client.GetAsync(Url));
        afterRemove.GetArrayLength().Should().Be(1);
        afterRemove[0].GetProperty("id").GetGuid().Should().Be(first);
        afterRemove[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Removing_an_unknown_method_is_404()
    {
        var response = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).DeleteAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
