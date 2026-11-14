using System.Net;
using System.Net.Http.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Shipping;

/// <summary>
/// The authorization of every Shipping route over real HTTP: anonymous callers get 401, members without the
/// Shipping permissions get 403 on every change, and an impersonated session can neither hand a shipment to customs
/// preparation nor submit a declaration. These checks run before the handler, so unknown ids are enough.
/// </summary>
[Collection(E2ECollection.Name)]
public class ShippingEndpointAuthorizationTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/shipping";

    private static object NewShipment() => new
    {
        reference = "SHP-" + Guid.NewGuid().ToString("N")[..8],
        originCountry = "CN",
        destinationCountry = "AE",
        hsCode = "8471.30",
        declaredValue = 1000m,
        currency = "USD",
        weightKg = 120m
    };

    [Fact]
    public async Task Anonymous_callers_are_refused_on_every_route()
    {
        var anonymous = fixture.CreateAnonymousClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync($"{Base}/shipments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Base}/shipments/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Base}/shipments/{id}/duties")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Base}/shipments/{id}/declaration")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Base}/shipments", NewShipment())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"{Base}/shipments/{id}/ready-for-clearance", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Base}/shipments/{id}/declaration", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"{Base}/declarations/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_without_the_shipping_permissions_may_read_but_not_change_anything()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        var id = Guid.NewGuid();

        (await user.GetAsync($"{Base}/shipments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.PostAsJsonAsync($"{Base}/shipments", NewShipment())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsync($"{Base}/shipments/{id}/ready-for-clearance", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync($"{Base}/shipments/{id}/declaration", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsync($"{Base}/declarations/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_impersonated_session_cannot_hand_over_a_shipment_or_submit_a_declaration()
    {
        var impersonated = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        impersonated.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, Guid.NewGuid().ToString());
        var id = Guid.NewGuid();

        var ready = await impersonated.PostAsync($"{Base}/shipments/{id}/ready-for-clearance", null);
        var submit = await impersonated.PostAsync($"{Base}/declarations/{id}/submit", null);

        ready.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        submit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await submit.Content.ReadAsStringAsync()).Should().Contain("impersonation");
    }
}
