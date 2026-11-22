using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Shared.Core.Authorization;
using Shipping.Contracts.Shipments;
using Xunit;

namespace Api.E2ETests.Shipping;

/// <summary>
/// The MCP server over HTTP (ADR-0034), spoken as an MCP client speaks it (JSON-RPC over Streamable HTTP): the
/// conversational sample <c>shipping.assistant</c> offers its read tools to a signed-in person; anonymous callers,
/// members who may not start agent runs, and agents that are not MCP servers get nothing.
/// </summary>
[Collection(AgentsEnabledE2ECollection.Name)]
public class ShippingAssistantMcpE2ETests(AgentsEnabledE2EFixture fixture)
{
    private const string Route = "/api/v1/mcp/shipping.assistant";

    private static HttpRequestMessage Rpc(string route, string method, object? parameters = null) => new(HttpMethod.Post, route)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }),
            Encoding.UTF8, "application/json"),
        Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json"), new MediaTypeWithQualityHeaderValue("text/event-stream") } }
    };

    /// <summary>The JSON-RPC message of a response, sent as JSON or as one server-sent event.</summary>
    private static async Task<JsonElement> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? string.Concat(body.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()))
            : body;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task APerson_ListsTheAssistantsReadTools_AndCallsOne_AsThemselves()
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);
        var reference = "MCP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = await client.PostAsJsonAsync("/api/v1/shipping/shipments", new
        {
            reference,
            originCountry = "CN",
            destinationCountry = "AE",
            hsCode = "8471.30",
            declaredValue = 1000m,
            currency = "USD",
            weightKg = 120m,
            documents = new[] { new { kind = ShipmentDocumentKind.CommercialInvoice, reference = "INV-1", content = "10 laptops." } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var listed = await MessageAsync(await client.SendAsync(Rpc(Route, "tools/list")));
        var tools = listed.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(
            ["shipping.find_shipments", "shipping.get_shipment", "shipping.estimate_duties", "shipping.check_documents"]);
        tools.Should().OnlyContain(t => t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());

        var called = await MessageAsync(await client.SendAsync(Rpc(Route, "tools/call",
            new { name = "shipping.find_shipments", arguments = new { reference } })));
        var result = called.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain(reference);

        // The call is on record as a run of the person, with trigger Mcp.
        var runs = (await client.GetFromJsonAsync<JsonElement>("/api/v1/agent-runs?pageSize=100")).GetProperty("data")
            .GetProperty("result").EnumerateArray();
        runs.Should().Contain(r => r.GetProperty("agentKey").GetString() == "shipping.assistant" &&
                                   r.GetProperty("triggerKind").GetInt32() == 6);
    }

    [Fact]
    public async Task AnAgentThatIsNotAnMcpServer_AnswersWithAnError()
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);

        var listed = await MessageAsync(await client.SendAsync(Rpc("/api/v1/mcp/shipping.shipment-preparation", "tools/list")));

        listed.TryGetProperty("error", out _).Should().BeTrue(listed.GetRawText());
    }

    [Fact]
    public async Task AnonymousCallers_AndMembersWhoMayNotStartRuns_AreRefused()
    {
        var anonymous = await fixture.Factory.CreateClient().SendAsync(Rpc(Route, "tools/list"));
        var member = await fixture.Factory.CreateClientAs(await fixture.SeedUserAsync(RoleConstants.User), RoleConstants.User)
            .SendAsync(Rpc(Route, "tools/list"));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        member.StatusCode.Should().Be(HttpStatusCode.Forbidden, await member.Content.ReadAsStringAsync());
    }
}
