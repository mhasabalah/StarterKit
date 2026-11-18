using FluentValidation;
using Shipping.Contracts.Declarations;
using Shipping.Contracts.Shipments;
using Shipping.Domains.Agents;
using Shipping.Domains.Declarations.Features;
using Shipping.Domains.Declarations.Models;
using Shipping.Domains.Shipments.Features;
using Shipping.Domains.Shipments.Models;

namespace Shipping.Tests;

/// <summary>The validators reject bad input with their own localized message (never FluentValidation's default text).</summary>
public class ShippingValidatorTests
{
    private static readonly ShipmentDocumentInput[] Documents =
        [new(ShipmentDocumentKind.CommercialInvoice, "INV-1", "10 laptops.")];

    private static CreateShipmentCommand ValidShipment() =>
        new("SHP-1", "CN", "AE", "8471.30", 1000m, "USD", 120m, Documents);

    private static string Message(string key) => TestSupport.Localizer()[key].Value;

    private static void ShouldFailWith<T>(AbstractValidator<T> validator, T instance, string key)
    {
        var result = validator.Validate(instance);
        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.ErrorMessage).Should().Contain(Message(key));
    }

    [Fact]
    public void CreateShipment_AcceptsAValidCommand() =>
        new CreateShipmentCommandValidator(TestSupport.Localizer()).Validate(ValidShipment()).IsValid.Should().BeTrue();

    public static TheoryData<string> InvalidShipmentFields =>
        ["reference", "longReference", "origin", "destination", "hsCode", "currency", "value", "weight", "documents",
            "documentReference", "documentContent"];

    [Theory]
    [MemberData(nameof(InvalidShipmentFields))]
    public void CreateShipment_RejectsAnInvalidField_WithItsLocalizedMessage(string field)
    {
        var valid = ValidShipment();
        var (command, key) = field switch
        {
            "reference" => (valid with { Reference = "" }, "Validation.Reference.Required"),
            "longReference" => (valid with { Reference = new string('A', Shipment.ReferenceMaxLength + 1) }, "Validation.Reference.MaxLength"),
            "origin" => (valid with { OriginCountry = "China" }, "Validation.Country.Format"),
            "destination" => (valid with { DestinationCountry = "A1" }, "Validation.Country.Format"),
            "hsCode" => (valid with { HsCode = "84" }, "Validation.HsCode.Format"),
            "currency" => (valid with { Currency = "DOLLAR" }, "Validation.Currency.Format"),
            "value" => (valid with { DeclaredValue = 0m }, "Validation.Amount.Positive"),
            "weight" => (valid with { WeightKg = -1m }, "Validation.Amount.Positive"),
            "documents" => (valid with
            {
                Documents = Enumerable.Range(0, Shipment.MaxDocuments + 1)
                    .Select(i => new ShipmentDocumentInput(ShipmentDocumentKind.PackingList, $"PL-{i}", null)).ToList()
            }, "Validation.Documents.Max"),
            "documentReference" => (valid with { Documents = [new(ShipmentDocumentKind.PackingList, " ", null)] },
                "Validation.Document.Reference"),
            _ => (valid with
            {
                Documents = [new(ShipmentDocumentKind.PackingList, "PL-1", new string('x', ShipmentDocument.ContentMaxLength + 1))]
            }, "Validation.Document.Content")
        };

        ShouldFailWith(new CreateShipmentCommandValidator(TestSupport.Localizer()), command, key);
    }

    [Fact]
    public void StateTransitions_RejectAnEmptyId_AndANegativeVersion()
    {
        var ready = new MarkShipmentReadyForClearanceCommandValidator(TestSupport.Localizer());
        var submit = new SubmitDeclarationCommandValidator(TestSupport.Localizer());

        ShouldFailWith(ready, new MarkShipmentReadyForClearanceCommand(Guid.Empty), "Validation.Id.Required");
        ShouldFailWith(ready, new MarkShipmentReadyForClearanceCommand(Guid.NewGuid(), -1), "Validation.Version.Min");
        ShouldFailWith(submit, new SubmitDeclarationCommand(Guid.Empty), "Validation.Id.Required");
        ShouldFailWith(submit, new SubmitDeclarationCommand(Guid.NewGuid(), -1), "Validation.Version.Min");
        submit.Validate(new SubmitDeclarationCommand(Guid.NewGuid(), 3)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PrepareDeclaration_RejectsOverlongNotes() =>
        ShouldFailWith(new PrepareDeclarationDraftCommandValidator(TestSupport.Localizer()),
            new PrepareDeclarationDraftCommand(Guid.NewGuid(), new string('n', DeclarationDraft.NotesMaxLength + 1)),
            "Validation.Notes.MaxLength");

    [Fact]
    public void TheAgentResult_NeedsAReference_AndACustomsReferenceOnceSubmitted()
    {
        var validator = new ShipmentPreparationResultValidator(TestSupport.Localizer());
        var valid = new ShipmentPreparationResult("SHP-1", ["All documents present."], [], 0m, 50m, "USD", Guid.NewGuid(),
            Submitted: true, SubmissionReference: "MOCK-1");

        validator.Validate(valid).IsValid.Should().BeTrue();
        ShouldFailWith(validator, valid with { ShipmentReference = "" }, "Validation.Reference.Required");
        ShouldFailWith(validator, valid with { ShipmentReference = new string('A', Shipment.ReferenceMaxLength + 1) },
            "Validation.Reference.MaxLength");
        ShouldFailWith(validator, valid with { SubmissionReference = null }, "Validation.Result.SubmissionReference");
        ShouldFailWith(validator, valid with { Findings = Enumerable.Repeat("x", 21).ToList() }, "Validation.Result.Findings");
    }
}
