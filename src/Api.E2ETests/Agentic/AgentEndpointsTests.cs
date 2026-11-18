using System.Net;
using System.Net.Http.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Agentic;

/// <summary>
/// The agent routes over real HTTP against the booted API in its DEFAULT configuration (<c>Agentic:Enabled=false</c>):
/// the module is wired into the host (its migrations ran, its routes are mapped and protected), and while it is
/// switched off the routes do not exist for anyone.
/// </summary>
[Collection(E2ECollection.Name)]
public class AgentEndpointsTests(E2ETestFixture fixture)
{
    public static TheoryData<string> ReadRoutes => new(
        "/api/v1/agents",
        "/api/v1/agents/shipping.shipment-preparation",
        "/api/v1/agent-runs",
        $"/api/v1/agent-runs/{Guid.NewGuid()}",
        $"/api/v1/agent-runs/{Guid.NewGuid()}/steps",
        "/api/v1/agent-approvals",
        $"/api/v1/agent-approvals/{Guid.NewGuid()}",
        "/api/v1/agent-policies/any.agent",
        "/api/v1/agent-usage",
        "/api/v1/knowledge-documents",
        "/api/v1/agent-memories");

    [Theory]
    [MemberData(nameof(ReadRoutes))]
    public async Task Anonymous_callers_are_rejected_before_anything_else(string route)
    {
        var response = await fixture.CreateAnonymousClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(ReadRoutes))]
    public async Task While_the_module_is_switched_off_its_routes_are_not_found(string route)
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

        var response = await admin.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task While_the_module_is_switched_off_a_run_cannot_be_started()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

        var response = await admin.PostAsJsonAsync("/api/v1/agent-runs", new { agentKey = "any.agent", prompt = "Do it" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancel_is_rejected_for_anonymous_callers_and_not_found_while_the_module_is_off()
    {
        var route = $"/api/v1/agent-runs/{Guid.NewGuid()}/cancel";

        var anonymous = await fixture.CreateAnonymousClient().PostAsJsonAsync(route, new { });
        var admin = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin).PostAsJsonAsync(route, new { });

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        admin.StatusCode.Should().Be(HttpStatusCode.NotFound, await admin.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string> DecisionRoutes => new()
    {
        { "POST", $"/api/v1/agent-approvals/{Guid.NewGuid()}/approve" },
        { "POST", $"/api/v1/agent-approvals/{Guid.NewGuid()}/reject" },
        { "PUT", "/api/v1/agent-policies/any.agent" },
        { "POST", "/api/v1/agent-memories" },
        { "DELETE", $"/api/v1/agent-memories/{Guid.NewGuid()}" }
    };

    [Theory]
    [MemberData(nameof(DecisionRoutes))]
    public async Task Governance_routes_reject_anonymous_callers_and_users_without_the_permission(string method, string route)
    {
        var body = JsonContent.Create(new { payloadHash = new string('0', 64), enabled = true });

        var anonymous = await fixture.CreateAnonymousClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), route) { Content = body });
        var user = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User)
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), route) { Content = JsonContent.Create(new { payloadHash = new string('0', 64), enabled = true }) });
        var admin = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin)
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), route) { Content = JsonContent.Create(new { payloadHash = new string('0', 64), enabled = true }) });

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        user.StatusCode.Should().Be(HttpStatusCode.Forbidden, await user.Content.ReadAsStringAsync());
        admin.StatusCode.Should().Be(HttpStatusCode.NotFound, "the module is switched off: " + await admin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_user_without_the_permission_cannot_start_a_run()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        var response = await user.PostAsJsonAsync("/api/v1/agent-runs", new { agentKey = "any.agent", prompt = "Do it" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }
}
