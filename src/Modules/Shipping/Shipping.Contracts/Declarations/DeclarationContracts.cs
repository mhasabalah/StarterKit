using Shared.Contracts.Attributes;
using Shared.Contracts.CQRS;

namespace Shipping.Contracts.Declarations;

public enum DeclarationStatus
{
    [LocalizedDescription("Draft", "مسودة")]
    Draft = 1,

    [LocalizedDescription("Submitted", "مقدَّم")]
    Submitted = 2
}

/// <summary>Duties and taxes computed by the deterministic tariff rules (never by a model).</summary>
public record DutyEstimateDto(
    string HsCode,
    string DestinationCountry,
    decimal CustomsValue,
    decimal DutyRate,
    decimal Duty,
    decimal VatRate,
    decimal Vat,
    decimal Total,
    string Currency);

public record DeclarationDraftDto(
    Guid Id,
    Guid ShipmentId,
    string HsCode,
    decimal CustomsValue,
    decimal Duty,
    decimal Vat,
    string Currency,
    string? Notes,
    DeclarationStatus Status,
    string? SubmissionReference,
    DateTime? SubmittedAt,
    long Version);

public record EstimateDutiesQuery(Guid ShipmentId) : IQuery<DutyEstimateDto>;

/// <summary>
/// Creates the shipment's declaration draft, or recomputes it while it is still a draft. Amounts always come from the
/// tariff rules; <paramref name="Notes"/> is free text for the customs broker. Repeating it changes nothing new.
/// </summary>
public record PrepareDeclarationDraftCommand(Guid ShipmentId, string? Notes = null) : ICommand<DeclarationDraftDto>;

/// <summary>
/// Submits the declaration to customs (an external, irreversible action). Idempotent: a submitted declaration is
/// returned as it is, and the customs gateway receives the declaration id as idempotency key.
/// </summary>
public record SubmitDeclarationCommand(Guid DeclarationId, long? Version = null) : ICommand<DeclarationDraftDto>;

public record GetDeclarationByShipmentQuery(Guid ShipmentId) : IQuery<DeclarationDraftDto>;
