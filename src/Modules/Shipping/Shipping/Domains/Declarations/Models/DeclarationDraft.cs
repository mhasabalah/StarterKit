using Shipping.Contracts.Declarations;

namespace Shipping.Domains.Declarations.Models;

/// <summary>
/// The customs declaration of one shipment (one per shipment). While it is a draft it can be recomputed; once
/// submitted it is final and carries the customs reference. Amounts always come from <c>DutyCalculator</c>.
/// </summary>
public class DeclarationDraft : Aggregate<Guid>, IMustHaveTenant
{
    public const int NotesMaxLength = 1_000;
    public const int SubmissionReferenceMaxLength = 100;

    public Guid TenantId { get; set; }
    public Guid ShipmentId { get; private set; }
    public string HsCode { get; private set; } = default!;
    public decimal CustomsValue { get; private set; }
    public decimal Duty { get; private set; }
    public decimal Vat { get; private set; }
    public string Currency { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DeclarationStatus Status { get; private set; }
    public string? SubmissionReference { get; private set; }
    public DateTime? SubmittedAt { get; private set; }

    private DeclarationDraft()
    {
    }

    public static DeclarationDraft Prepare(Guid shipmentId, DutyEstimateDto estimate, string? notes)
    {
        var draft = new DeclarationDraft { Id = Guid.CreateVersion7(), ShipmentId = shipmentId, Status = DeclarationStatus.Draft };
        draft.Apply(estimate, notes);
        return draft;
    }

    public void Recompute(DutyEstimateDto estimate, string? notes)
    {
        EnsureDraft();
        Apply(estimate, notes);
    }

    public void MarkSubmitted(string submissionReference, DateTime now)
    {
        EnsureDraft();
        Status = DeclarationStatus.Submitted;
        SubmissionReference = submissionReference.Length <= SubmissionReferenceMaxLength
            ? submissionReference
            : submissionReference[..SubmissionReferenceMaxLength];
        SubmittedAt = now;
    }

    private void EnsureDraft()
    {
        if (Status != DeclarationStatus.Draft)
            throw new InvalidShipmentStateException("declaration_already_submitted");
    }

    private void Apply(DutyEstimateDto estimate, string? notes)
    {
        HsCode = estimate.HsCode;
        CustomsValue = estimate.CustomsValue;
        Duty = estimate.Duty;
        Vat = estimate.Vat;
        Currency = estimate.Currency;
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Notes = trimmed is { Length: > NotesMaxLength } ? trimmed[..NotesMaxLength] : trimmed;
    }
}
