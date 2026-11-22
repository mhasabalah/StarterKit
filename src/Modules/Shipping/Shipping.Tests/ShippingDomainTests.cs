using System.Text.Json;
using Agentic.Contracts.Scripting;
using Shipping.Contracts.Declarations;
using Shipping.Contracts.Shipments;
using Shipping.Contracts.Shipments.IntegrationEvents;
using Shipping.Domains.Agents;
using Shipping.Domains.Declarations.Models;
using Shipping.Domains.Declarations.Services;
using Shipping.Domains.Shipments.Exceptions;
using Shipping.Domains.Shipments.Models;

namespace Shipping.Tests;

public class ShippingDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly ShipmentDocumentInput[] AllDocuments =
    [
        new(ShipmentDocumentKind.CommercialInvoice, "INV-1", "Invoice for 10 laptops."),
        new(ShipmentDocumentKind.PackingList, "PL-1", null),
        new(ShipmentDocumentKind.CertificateOfOrigin, "CO-1", "Origin: China.")
    ];

    private static Shipment NewShipment(string hsCode = "8471.30", string destination = "AE", decimal value = 1000m,
        IEnumerable<ShipmentDocumentInput>? documents = null) =>
        Shipment.Create(" shp-001 ", "cn", destination, hsCode, value, "usd", 120m, documents ?? AllDocuments);

    // ----- shipment ---------------------------------------------------------------------------------------------

    [Fact]
    public void Create_NormalizesCodes_AndStartsAsADraft()
    {
        var shipment = NewShipment();

        shipment.Reference.Should().Be("SHP-001");
        shipment.OriginCountry.Should().Be("CN");
        shipment.Currency.Should().Be("USD");
        shipment.Status.Should().Be(ShipmentStatus.Draft);
        shipment.Documents.Should().HaveCount(3);
        shipment.MissingDocuments().Should().BeEmpty();
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("originCountry")]
    [InlineData("hsCode")]
    [InlineData("declaredValue")]
    [InlineData("sameCountry")]
    public void Create_RefusesAnInvalidField(string field)
    {
        var act = () => Shipment.Create(
            field == "reference" ? "bad ref!" : "SHP-1",
            field == "originCountry" ? "CHN" : "CN",
            field == "sameCountry" ? "CN" : "AE",
            field == "hsCode" ? "84" : "8471",
            field == "declaredValue" ? 0 : 10,
            "USD", 1, null);

        act.Should().Throw<InvalidShipmentException>().Which.ErrorCode.Should().Be("invalid_shipment");
    }

    [Fact]
    public void MissingDocuments_ListsTheRequiredOnesItLacks()
    {
        var shipment = NewShipment(documents: [AllDocuments[0]]);

        shipment.MissingDocuments().Should().Equal(ShipmentDocumentKind.PackingList, ShipmentDocumentKind.CertificateOfOrigin);
    }

    [Fact]
    public void MarkReadyForClearance_RaisesTheTriggerEvent_ForTheRequester_AndOnlyOnce()
    {
        var shipment = NewShipment();
        var user = Guid.NewGuid();

        shipment.MarkReadyForClearance(user);

        shipment.Status.Should().Be(ShipmentStatus.ReadyForClearance);
        var raised = shipment.IntegrationEvents.Should().ContainSingle().Which.Should()
            .BeOfType<ShipmentReadyForClearanceIntegrationEvent>().Subject;
        raised.ShipmentId.Should().Be(shipment.Id);
        raised.RequestedBy.Should().Be(user);
        FluentActions.Invoking(() => shipment.MarkReadyForClearance(user))
            .Should().Throw<InvalidShipmentStateException>().Which.ErrorCode.Should().Be("shipment_not_draft");
    }

    [Fact]
    public void ADeclaration_CannotBeSubmittedForAShipmentWithMissingDocuments()
    {
        var shipment = NewShipment(documents: [AllDocuments[0]]);
        shipment.MarkReadyForClearance(Guid.NewGuid());

        FluentActions.Invoking(shipment.MarkDeclarationSubmitted)
            .Should().Throw<InvalidShipmentStateException>().Which.ErrorCode.Should().Be("documents_missing");
    }

    [Fact]
    public void ADraftShipment_IsNotReadyForADeclaration() =>
        FluentActions.Invoking(NewShipment().EnsureReadyForClearance)
            .Should().Throw<InvalidShipmentStateException>().Which.ErrorCode.Should().Be("shipment_not_ready");

    // ----- tariff rules -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("8471.30", "AE", 1000, 0, 50)]        // computers: no duty, UAE VAT 5% of 1000
    [InlineData("6109.10", "SA", 1000, 120, 168)]     // apparel 12%, Saudi VAT 15% of 1120
    [InlineData("9503.00", "JP", 333.33, 16.67, 35)]  // default 5% duty and 10% VAT, rounded half away from zero
    public void Duties_FollowTheTariffRules(string hsCode, string destination, decimal value, decimal duty, decimal vat)
    {
        var estimate = DutyCalculator.Estimate(NewShipment(hsCode, destination, value));

        estimate.Duty.Should().Be(duty);
        estimate.Vat.Should().Be(vat);
        estimate.Total.Should().Be(duty + vat);
        estimate.CustomsValue.Should().Be(value);
        estimate.Currency.Should().Be("USD");
    }

    // ----- declaration ------------------------------------------------------------------------------------------

    [Fact]
    public void ADraft_IsRecomputable_ButASubmittedDeclarationIsFinal()
    {
        var estimate = DutyCalculator.Estimate(NewShipment());
        var draft = DeclarationDraft.Prepare(Guid.NewGuid(), estimate, "  First  ");
        draft.Notes.Should().Be("First");

        draft.Recompute(estimate, "Second");
        draft.MarkSubmitted("MOCK-123", Now);

        draft.Status.Should().Be(DeclarationStatus.Submitted);
        draft.SubmissionReference.Should().Be("MOCK-123");
        FluentActions.Invoking(() => draft.Recompute(estimate, null))
            .Should().Throw<InvalidShipmentStateException>().Which.ErrorCode.Should().Be("declaration_already_submitted");
    }

    [Fact]
    public async Task TheMockGateway_IsIdempotentPerKey()
    {
        var gateway = new MockCustomsGateway();
        var submission = new CustomsSubmission(Guid.NewGuid(), "SHP-1", "8471", 10, 0, 1, "USD");

        var first = await gateway.SubmitAsync(submission, "key-1", CancellationToken.None);
        var again = await gateway.SubmitAsync(submission, "key-1", CancellationToken.None);
        var other = await gateway.SubmitAsync(submission, "key-2", CancellationToken.None);

        first.Should().StartWith("MOCK-").And.Be(again);
        other.Should().NotBe(first);
        gateway.Received.Should().Be(2);
    }

    // ----- the scripted scenario --------------------------------------------------------------------------------

    private static readonly Guid ShipmentId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid DeclarationId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static ScriptedReply Step(params (string Tool, object Result)[] results) =>
        new ShipmentPreparationScript().Next(new ScriptedTurn(ShipmentPreparationAgent.Key,
            $"Prepare it.\n\nInput (JSON):\n{ShipmentPreparationAgent.InputFor(ShipmentId)}", results.Length + 1,
            results.Select(r => new ScriptedToolResult(r.Tool, JsonSerializer.Serialize(r.Result, JsonSerializerOptions.Web))).ToList()));

    private static readonly (string, object) Shipment1 = ("shipping.get_shipment", new { reference = "SHP-001" });
    private static readonly (string, object) Estimate1 = ("shipping.estimate_duties", new { duty = 0m, vat = 50m, currency = "USD" });
    private static readonly (string, object) Draft1 = ("shipping.prepare_declaration", new { id = DeclarationId });

    [Fact]
    public void TheScript_WalksTheSteps_InOrder()
    {
        Step().ToolCalls!.Single().Tool.Should().Be("shipping.get_shipment");
        Step(Shipment1).ToolCalls!.Single().Tool.Should().Be("shipping.estimate_duties");
        Step(Shipment1, Estimate1).ToolCalls!.Single().Tool.Should().Be("shipping.prepare_declaration");
        Step(Shipment1, Estimate1, Draft1).ToolCalls!.Single().Tool.Should().Be("shipping.check_documents");

        var submit = Step(Shipment1, Estimate1, Draft1, ("shipping.check_documents", new { missing = Array.Empty<string>() }))
            .ToolCalls!.Single();
        submit.Tool.Should().Be("shipping.submit_declaration");
        submit.ArgumentsJson.Should().Contain(DeclarationId.ToString());
    }

    [Fact]
    public void TheScript_DoesNotSubmit_WhenADocumentIsMissing()
    {
        var reply = Step(Shipment1, Estimate1, Draft1, ("shipping.check_documents", new { missing = new[] { "CertificateOfOrigin" } }));

        var call = reply.ToolCalls!.Single();
        call.Tool.Should().Be("agent.submit_result");
        var result = JsonDocument.Parse(call.ArgumentsJson).RootElement;
        result.GetProperty("submitted").GetBoolean().Should().BeFalse();
        result.GetProperty("missingDocuments")[0].GetString().Should().Be("CertificateOfOrigin");
    }

    [Fact]
    public void TheScript_ReportsARejectedSubmission()
    {
        var reply = Step(Shipment1, Estimate1, Draft1, ("shipping.check_documents", new { missing = Array.Empty<string>() }),
            ("shipping.submit_declaration", new { error = new { code = "rejected", message = "No." } }));

        var result = JsonDocument.Parse(reply.ToolCalls!.Single().ArgumentsJson).RootElement;
        result.GetProperty("submitted").GetBoolean().Should().BeFalse();
        result.GetProperty("findings")[0].GetString().Should().Contain("rejected");
    }

    // ----- the conversational assistant's scripted scenario ------------------------------------------------------

    private static ScriptedReply Ask(string prompt, IReadOnlyList<ScriptedHistoryTurn>? history = null,
        params (string Tool, object Result)[] results) =>
        new ShippingAssistantScript().Next(new ScriptedTurn(ShippingAssistantAgent.Key, prompt, results.Length + 1,
            results.Select(r => new ScriptedToolResult(r.Tool, JsonSerializer.Serialize(r.Result, JsonSerializerOptions.Web))).ToList(),
            history));

    private static readonly (string, object) FoundShp1 = ("shipping.find_shipments", new
    {
        shipments = new[] { new { id = ShipmentId, reference = "SHP-001", originCountry = "CN", destinationCountry = "AE", hsCode = "8471.30", status = "ReadyForClearance" } }
    });

    [Fact]
    public void TheAssistant_FindsTheShipmentNamedInTheMessage_ThenAnswersFromIt()
    {
        var find = Ask("Where is SHP-001?").ToolCalls!.Single();
        find.Tool.Should().Be("shipping.find_shipments");
        find.ArgumentsJson.Should().Contain("SHP-001");

        Ask("Where is SHP-001?", null, FoundShp1).Text.Should().Contain("CN").And.Contain("AE").And.Contain("ReadyForClearance");
    }

    [Fact]
    public void TheAssistant_TakesTheShipmentOfAFollowUp_FromTheEarlierTurns()
    {
        var history = new[] { new ScriptedHistoryTurn("Where is SHP-001?", "SHP-001 goes from CN to AE.") };

        Ask("And how much VAT is due?", history).ToolCalls!.Single().ArgumentsJson.Should().Contain("SHP-001");
        var estimate = Ask("And how much VAT is due?", history, FoundShp1).ToolCalls!.Single();
        estimate.Tool.Should().Be("shipping.estimate_duties");
        estimate.ArgumentsJson.Should().Contain(ShipmentId.ToString());

        Ask("And how much VAT is due?", history, FoundShp1,
                ("shipping.estimate_duties", new { duty = 0m, vat = 50m, total = 50m, currency = "USD" }))
            .Text.Should().Contain("VAT 50.00 USD");
    }

    [Fact]
    public void TheAssistant_AsksWhichShipment_WhenNoneWasNamed_AndSaysWhenItDoesNotExist()
    {
        Ask("What is the status?").Text.Should().Contain("Which shipment");
        Ask("Where is SHP-404?", null, ("shipping.find_shipments", new { shipments = Array.Empty<object>() }))
            .Text.Should().Contain("no shipment").And.Contain("SHP-404");
    }
}
