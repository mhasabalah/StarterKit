using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using FluentAssertions;
using Shared.Core.Authorization;
using Shipping.Contracts.Shipments;
using Xunit;

namespace Api.E2ETests.Shipping;

/// <summary>
/// The conversational sample end to end, over HTTP with agents on (scripted model): a person asks the shipping assistant
/// about a shipment by its reference, then asks a follow-up that names no shipment; the second turn is answered from
/// the conversation's history. Then the conversation is deleted and no longer exists.
/// </summary>
[Collection(AgentsEnabledE2ECollection.Name)]
public class ShippingAssistantConversationE2ETests(AgentsEnabledE2EFixture fixture)
{
    private const string Conversations = "/api/v1/agent-conversations";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task AFollowUpQuestion_IsAnsweredFromTheConversation_AndDeletingItRemovesIt()
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);
        var reference = "CNV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
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

        // Turn 1: the shipment is named.
        var started = await client.PostAsJsonAsync(Conversations,
            new { agentKey = "shipping.assistant", message = $"Where is {reference}?", title = "Customs questions" });
        started.StatusCode.Should().Be(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        var turn1 = await DataAsync(started);
        var conversationId = turn1.GetProperty("conversationId").GetGuid();
        (await WaitForStatusAsync(client, turn1.GetProperty("run").GetProperty("id").GetGuid())).Should().Be("Completed");

        // Turn 2: a follow-up that names no shipment.
        var sent = await client.PostAsJsonAsync($"{Conversations}/{conversationId}/messages", new { message = "And how much VAT is due?" });
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());
        var turn2 = await DataAsync(sent);
        (await WaitForStatusAsync(client, turn2.GetProperty("run").GetProperty("id").GetGuid())).Should().Be("Completed");

        var detail = await DataAsync(await client.GetAsync($"{Conversations}/{conversationId}"));
        detail.GetProperty("conversation").GetProperty("title").GetString().Should().Be("Customs questions");
        detail.GetProperty("conversation").GetProperty("messageCount").GetInt32().Should().Be(2);
        var messages = detail.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("message").GetString()).Should().Equal($"Where is {reference}?", "And how much VAT is due?");
        messages[0].GetProperty("answer").GetString().Should().Contain("CN").And.Contain("AE");
        messages[1].GetProperty("answer").GetString().Should().Contain(reference).And.Contain("VAT 50.00 USD");

        var mine = await DataAsync(await client.GetAsync($"{Conversations}?agentKey=shipping.assistant"));
        mine.GetProperty("result").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(conversationId);

        // Someone else in the tenant without agentic.runs.read does not see it.
        var other = await fixture.SeedUserAsync(RoleConstants.User);
        var otherClient = fixture.Factory.CreateClientAs(other, RoleConstants.User);
        (await otherClient.GetAsync($"{Conversations}/{conversationId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.DeleteAsync($"{Conversations}/{conversationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{Conversations}/{conversationId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AskingToPrepareAShipment_DelegatesToThePreparationAgent_WhoseSubmissionAPersonApproves()
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);
        var reference = "DLG-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = await client.PostAsJsonAsync("/api/v1/shipping/shipments", new
        {
            reference,
            originCountry = "CN",
            destinationCountry = "AE",
            hsCode = "8471.30",
            declaredValue = 1000m,
            currency = "USD",
            weightKg = 120m,
            documents = new[]
            {
                new { kind = ShipmentDocumentKind.CommercialInvoice, reference = "INV-1", content = "10 laptops at 100 USD each." },
                new { kind = ShipmentDocumentKind.PackingList, reference = "PL-1", content = "1 pallet, 120 kg." },
                new { kind = ShipmentDocumentKind.CertificateOfOrigin, reference = "CO-1", content = "Made in China." }
            }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var shipmentId = (await DataAsync(created)).GetProperty("id").GetGuid();

        // Handing it over starts the automatic preparation; the person rejects that submission, so the shipment waits.
        (await client.PostAsJsonAsync($"/api/v1/shipping/shipments/{shipmentId}/ready-for-clearance", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var automatic = await PreparationRunAsync(client, async run =>
            Trigger(run) == AgentTriggerKind.Event &&
            (await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{run.GetProperty("id").GetGuid()}")))
                .GetProperty("inputJson").GetString()?.Contains(shipmentId.ToString(), StringComparison.Ordinal) == true);
        (await WaitForStatusAsync(client, automatic)).Should().Be("AwaitingApproval");
        var first = await PendingApprovalAsync(client, automatic);
        (await client.PostAsJsonAsync($"/api/v1/agent-approvals/{first.GetProperty("id").GetGuid()}/reject",
            new { reason = "Not yet." })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WaitForStatusAsync(client, automatic)).Should().Be("Completed");

        // The assistant hands the preparation to the preparation agent; its submission waits for the same person.
        var started = await client.PostAsJsonAsync(Conversations,
            new { agentKey = "shipping.assistant", message = $"Please prepare {reference} for clearance." });
        started.StatusCode.Should().Be(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        var parentId = (await DataAsync(started)).GetProperty("run").GetProperty("id").GetGuid();
        var delegated = await PreparationRunAsync(client, run => Task.FromResult(
            Trigger(run) == AgentTriggerKind.Delegated && run.GetProperty("parentRunId").ValueKind == JsonValueKind.String &&
            run.GetProperty("parentRunId").GetGuid() == parentId));
        (await WaitForStatusAsync(client, delegated)).Should().Be("AwaitingApproval");
        (await StatusOfAsync(client, parentId)).Should().Be("AwaitingDelegate", "the assistant waits for its delegate");

        var approval = await PendingApprovalAsync(client, delegated);
        approval.GetProperty("toolName").GetString().Should().Be("shipping.submit_declaration");
        (await client.PostAsJsonAsync($"/api/v1/agent-approvals/{approval.GetProperty("id").GetGuid()}/approve",
                new { payloadHash = approval.GetProperty("payloadHash").GetString() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await WaitForStatusAsync(client, delegated)).Should().Be("Completed");
        (await WaitForStatusAsync(client, parentId)).Should().Be("Completed");
        var parent = await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{parentId}"));
        parent.GetProperty("outputText").GetString().Should().Contain($"{reference} was prepared and submitted");
        var declaration = await DataAsync(await client.GetAsync($"/api/v1/shipping/shipments/{shipmentId}/declaration"));
        declaration.GetProperty("submissionReference").GetString().Should().StartWith("MOCK-");
    }

    [Fact]
    public async Task ATaskAgent_CannotBeTalkedTo()
    {
        var user = await fixture.SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.Factory.CreateClientAs(user, RoleConstants.User, RoleConstants.Admin);

        var response = await client.PostAsJsonAsync(Conversations,
            new { agentKey = "shipping.shipment-preparation", message = "Hello" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("agent_not_conversational");
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private static AgentTriggerKind Trigger(JsonElement run)
    {
        var value = run.GetProperty("triggerKind");
        return value.ValueKind == JsonValueKind.Number
            ? (AgentTriggerKind)value.GetInt32()
            : Enum.Parse<AgentTriggerKind>(value.GetString()!);
    }

    private static async Task<string> StatusOfAsync(HttpClient client, Guid runId)
    {
        var value = (await DataAsync(await client.GetAsync($"/api/v1/agent-runs/{runId}"))).GetProperty("run").GetProperty("status");
        return value.ValueKind == JsonValueKind.Number ? ((AgentRunStatus)value.GetInt32()).ToString() : value.GetString()!;
    }

    /// <summary>The preparation run that matches <paramref name="match"/>, waiting until it exists.</summary>
    private static async Task<Guid> PreparationRunAsync(HttpClient client, Func<JsonElement, Task<bool>> match)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var page = await DataAsync(await client.GetAsync("/api/v1/agent-runs?agentKey=shipping.shipment-preparation&pageSize=100"));
            foreach (var run in page.GetProperty("result").EnumerateArray())
            {
                if (await match(run))
                    return run.GetProperty("id").GetGuid();
            }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("No matching preparation run was started.");
            await Task.Delay(250);
        }
    }

    private static async Task<JsonElement> PendingApprovalAsync(HttpClient client, Guid runId)
    {
        var approvals = await DataAsync(await client.GetAsync("/api/v1/agent-approvals?status=Pending&pageSize=100"));
        return approvals.GetProperty("result").EnumerateArray().Single(a => a.GetProperty("runId").GetGuid() == runId);
    }

    private static async Task<string> WaitForStatusAsync(HttpClient client, Guid runId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var status = await StatusOfAsync(client, runId);
            if (status is "Completed" or "Failed" or "Cancelled" or "AwaitingApproval")
                return status;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {runId} is still {status}.");
            await Task.Delay(250);
        }
    }
}
