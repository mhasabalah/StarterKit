using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Agentic.Contracts.Runs;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Shipping.Contracts.Declarations;
using Shipping.Contracts.Shipments;
using Xunit;

namespace Api.E2ETests.Shipping;

/// <summary>The API host with agents switched on (scripted model): the proof-of-concept demo runs for real.</summary>
public sealed class AgentsEnabledE2ETestFactory(string connectionString) : E2ETestFactory(connectionString)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agentic:Enabled"] = "true",
            ["Agentic:Mcp:Enabled"] = "true"
        }));
    }
}

/// <summary>Own Postgres + own host with agents enabled; the real Hangfire servers, outbox and in-memory bus run.</summary>
public sealed class AgentsEnabledE2EFixture : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres = new();

    public AgentsEnabledE2ETestFactory Factory { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new AgentsEnabledE2ETestFactory(_postgres.ConnectionString);
        _ = Factory.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A real, active user in the directory (agent runs re-check their initiator there).</summary>
    public async Task<Guid> SeedUserAsync(params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = $"ag{Guid.NewGuid():N}", Email = $"ag-{Guid.NewGuid():N}@example.com",
            EmailConfirmed = true, IsActive = true, IsProfileComplete = true
        };
        (await users.CreateAsync(user, "P@ssw0rd!Strong1")).Succeeded.Should().BeTrue();
        (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        return user.Id;
    }
}

[CollectionDefinition(Name)]
public sealed class AgentsEnabledE2ECollection : ICollectionFixture<AgentsEnabledE2EFixture>
{
    public const string Name = "E2E agents enabled";
}

/// <summary>
/// The proof of concept end to end, over HTTP: a person hands a shipment to customs preparation; the outbox event
/// starts the preparation agent on their behalf; the agent (scripted model) reads the shipment, estimates duties,
/// prepares the declaration draft and checks the documents through typed tools; the submission waits for a person;
/// approval resumes the run, which submits once and completes with a typed result.
/// </summary>
[Collection(AgentsEnabledE2ECollection.Name)]
public class ShipmentPreparationAgentE2ETests(AgentsEnabledE2EFixture fixture)
{
    private const string Shipping = "/api/v1/shipping";
    private const string AgentKey = "shipping.shipment-preparation";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    private static readonly object[] AllDocuments =
    [
        new { kind = ShipmentDocumentKind.CommercialInvoice, reference = "INV-1", content = "10 laptops at 100 USD each." },
        new { kind = ShipmentDocumentKind.PackingList, reference = "PL-1", content = "1 pallet, 120 kg." },
        new { kind = ShipmentDocumentKind.CertificateOfOrigin, reference = "CO-1", content = "Made in China." }
    ];

    private async Task<(HttpClient Client, Guid ShipmentId)> HandOverAsync(object[] documents)
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);

        var created = await client.PostAsJsonAsync($"{Shipping}/shipments", new
        {
            reference = "SHP-" + Guid.NewGuid().ToString("N")[..8],
            originCountry = "CN",
            destinationCountry = "AE",
            hsCode = "8471.30",
            declaredValue = 1000m,
            currency = "USD",
            weightKg = 120m,
            documents
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var shipmentId = (await DataAsync(created)).GetProperty("id").GetGuid();

        var ready = await client.PostAsJsonAsync($"{Shipping}/shipments/{shipmentId}/ready-for-clearance", new { });
        ready.StatusCode.Should().Be(HttpStatusCode.OK, await ready.Content.ReadAsStringAsync());
        return (client, shipmentId);
    }

    [Fact]
    public async Task TheAgent_PreparesTheShipment_WaitsForApproval_ThenSubmitsOnce_AndCompletes()
    {
        var (client, shipmentId) = await HandOverAsync(AllDocuments);

        // The event trigger started the run; it stops before the external submission.
        var runId = await RunOfShipmentAsync(client, shipmentId);
        var waiting = await WaitForStatusAsync(client, runId, "AwaitingApproval", "Completed", "Failed");
        waiting.Should().Be("AwaitingApproval");

        var draft = await DataAsync(await client.GetAsync($"{Shipping}/shipments/{shipmentId}/declaration"));
        Name<DeclarationStatus>(draft.GetProperty("status")).Should().Be("Draft", "the agent prepared the draft without asking");
        draft.GetProperty("vat").GetDecimal().Should().Be(50m);

        var approval = await PendingApprovalAsync(client, runId);
        approval.GetProperty("toolName").GetString().Should().Be("shipping.submit_declaration");
        var approve = await client.PostAsJsonAsync($"/api/v1/agent-approvals/{approval.GetProperty("id").GetGuid()}/approve",
            new { payloadHash = approval.GetProperty("payloadHash").GetString() });
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        (await WaitForStatusAsync(client, runId, "Completed", "Failed")).Should().Be("Completed");
        var output = await OutputAsync(client, runId);
        output.GetProperty("submitted").GetBoolean().Should().BeTrue();
        output.GetProperty("submissionReference").GetString().Should().StartWith("MOCK-");
        output.GetProperty("estimatedVat").GetDecimal().Should().Be(50m);

        var submitted = await DataAsync(await client.GetAsync($"{Shipping}/shipments/{shipmentId}/declaration"));
        Name<DeclarationStatus>(submitted.GetProperty("status")).Should().Be("Submitted");
        submitted.GetProperty("submissionReference").GetString().Should().Be(output.GetProperty("submissionReference").GetString());
        Name<ShipmentStatus>((await DataAsync(await client.GetAsync($"{Shipping}/shipments/{shipmentId}"))).GetProperty("status"))
            .Should().Be("DeclarationSubmitted");
    }

    [Fact]
    public async Task WithADocumentMissing_TheAgentReportsIt_AndSubmitsNothing()
    {
        var (client, shipmentId) = await HandOverAsync([AllDocuments[0]]);

        var runId = await RunOfShipmentAsync(client, shipmentId);
        (await WaitForStatusAsync(client, runId, "Completed", "Failed", "AwaitingApproval")).Should().Be("Completed");

        var output = await OutputAsync(client, runId);
        output.GetProperty("submitted").GetBoolean().Should().BeFalse();
        output.GetProperty("missingDocuments").EnumerateArray().Select(m => m.GetString())
            .Should().BeEquivalentTo(["PackingList", "CertificateOfOrigin"]);
        Name<DeclarationStatus>((await DataAsync(await client.GetAsync($"{Shipping}/shipments/{shipmentId}/declaration")))
            .GetProperty("status")).Should().Be("Draft");
    }

    [Fact]
    public async Task WhenAPersonRejectsTheSubmission_NothingIsSubmitted_AndTheRunReportsIt()
    {
        var (client, shipmentId) = await HandOverAsync(AllDocuments);
        var runId = await RunOfShipmentAsync(client, shipmentId);
        (await WaitForStatusAsync(client, runId, "AwaitingApproval", "Completed", "Failed")).Should().Be("AwaitingApproval");

        var approval = await PendingApprovalAsync(client, runId);
        var reject = await client.PostAsJsonAsync($"/api/v1/agent-approvals/{approval.GetProperty("id").GetGuid()}/reject",
            new { reason = "The declared value looks wrong." });
        reject.StatusCode.Should().Be(HttpStatusCode.OK, await reject.Content.ReadAsStringAsync());

        (await WaitForStatusAsync(client, runId, "Completed", "Failed")).Should().Be("Completed");
        var output = await OutputAsync(client, runId);
        output.GetProperty("submitted").GetBoolean().Should().BeFalse();
        output.GetProperty("findings")[0].GetString().Should().Contain("rejected");
        Name<DeclarationStatus>((await DataAsync(await client.GetAsync($"{Shipping}/shipments/{shipmentId}/declaration")))
            .GetProperty("status")).Should().Be("Draft");
    }

    // ----- helpers --------------------------------------------------------------------------------------------------

    /// <summary>The API sends enums as numbers; tests compare names.</summary>
    private static string Name<TEnum>(JsonElement value) where TEnum : struct, Enum =>
        value.ValueKind == JsonValueKind.Number ? ((TEnum)(object)value.GetInt32()).ToString() : value.GetString()!;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    /// <summary>The run the event trigger started for the shipment (found by its input).</summary>
    private static async Task<Guid> RunOfShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var runs = await DataAsync(await client.GetAsync($"/api/v1/agent-runs?agentKey={AgentKey}&pageSize=100"));
            foreach (var run in runs.GetProperty("result").EnumerateArray())
            {
                var id = run.GetProperty("id").GetGuid();
                var detail = await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{id}"));
                if (detail.GetProperty("inputJson").GetString() is { } input && input.Contains(shipmentId.ToString(), StringComparison.Ordinal))
                    return id;
            }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No preparation run was started for shipment {shipmentId}.");
            await Task.Delay(250);
        }
    }

    private static async Task<string> WaitForStatusAsync(HttpClient client, Guid runId, params string[] statuses)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var detail = await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{runId}"));
            var status = Name<AgentRunStatus>(detail.GetProperty("run").GetProperty("status"));
            if (statuses.Contains(status))
                return status;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {runId} is still {status}.");
            await Task.Delay(250);
        }
    }

    private static async Task<JsonElement> PendingApprovalAsync(HttpClient client, Guid runId)
    {
        var approvals = await DataAsync(await client.GetAsync($"/api/v1/agent-approvals?status=Pending&pageSize=100"));
        return approvals.GetProperty("result").EnumerateArray().Single(a => a.GetProperty("runId").GetGuid() == runId);
    }

    private static async Task<JsonElement> OutputAsync(HttpClient client, Guid runId)
    {
        var detail = await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{runId}"));
        return JsonDocument.Parse(detail.GetProperty("outputJson").GetString()!).RootElement.Clone();
    }
}
