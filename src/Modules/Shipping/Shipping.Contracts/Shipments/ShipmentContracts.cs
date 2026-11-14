using Shared.Contracts.Attributes;
using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace Shipping.Contracts.Shipments;

public enum ShipmentStatus
{
    [LocalizedDescription("Draft", "مسودة")]
    Draft = 1,

    /// <summary>Handed to customs preparation: the shipment preparation agent starts.</summary>
    [LocalizedDescription("Ready for clearance", "جاهزة للتخليص")]
    ReadyForClearance = 2,

    [LocalizedDescription("Declaration submitted", "تم تقديم البيان")]
    DeclarationSubmitted = 3
}

public enum ShipmentDocumentKind
{
    [LocalizedDescription("Commercial invoice", "فاتورة تجارية")]
    CommercialInvoice = 1,

    [LocalizedDescription("Packing list", "قائمة التعبئة")]
    PackingList = 2,

    [LocalizedDescription("Certificate of origin", "شهادة المنشأ")]
    CertificateOfOrigin = 3,

    [LocalizedDescription("Bill of lading", "بوليصة الشحن")]
    BillOfLading = 4
}

/// <param name="Content">The document's text as supplied (e.g. extracted from a scan). Content the platform does not control.</param>
public record ShipmentDocumentDto(ShipmentDocumentKind Kind, string Reference, string? Content);

public record ShipmentDocumentInput(ShipmentDocumentKind Kind, string Reference, string? Content);

/// <summary>Read model of a shipment. <see cref="Version"/> goes back on status changes for optimistic concurrency.</summary>
public record ShipmentDto(
    Guid Id,
    string Reference,
    string OriginCountry,
    string DestinationCountry,
    string HsCode,
    decimal DeclaredValue,
    string Currency,
    decimal WeightKg,
    ShipmentStatus Status,
    IReadOnlyList<ShipmentDocumentDto> Documents,
    DateTime? CreatedAt,
    long Version);

/// <param name="Reference">The shipper's reference, unique in the tenant.</param>
/// <param name="HsCode">Harmonized System code, e.g. <c>8471.30</c>.</param>
public record CreateShipmentCommand(
    string Reference,
    string OriginCountry,
    string DestinationCountry,
    string HsCode,
    decimal DeclaredValue,
    string Currency,
    decimal WeightKg,
    IReadOnlyList<ShipmentDocumentInput>? Documents = null) : ICommand<CreateShipmentResult>;

public record CreateShipmentResult(Guid Id);

/// <summary>
/// Hands the shipment to customs preparation: raises <c>ShipmentReadyForClearanceIntegrationEvent</c>, whose consumer
/// starts the preparation agent on behalf of the caller.
/// </summary>
public record MarkShipmentReadyForClearanceCommand(Guid Id, long? Version = null) : ICommand<ShipmentDto>;

public record GetShipmentByIdQuery(Guid Id) : IQuery<ShipmentDto>;

/// <summary>Paged list, newest first; <see cref="Search"/> matches the reference.</summary>
public record GetShipmentsQuery : BaseQuery, IQuery<PaginatedResult<ShipmentDto>>
{
    public string? Search { get; init; }
}
