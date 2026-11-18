using System.Text.RegularExpressions;
using Shipping.Contracts.Shipments.IntegrationEvents;

namespace Shipping.Domains.Shipments.Models;

/// <summary>
/// A shipment that needs customs clearance (the sample aggregate of the agent proof of concept). Its documents are a
/// child collection. Business rules live here and in the commands, never in the agent's instructions: which documents
/// clearance needs, which transitions are allowed, and what the declaration amounts are (see <c>DutyCalculator</c>).
/// </summary>
public partial class Shipment : Aggregate<Guid>, IMustHaveTenant
{
    public const int ReferenceMaxLength = 50;
    public const int HsCodeMaxLength = 13;
    public const int MaxDocuments = 20;

    /// <summary>The documents customs clearance needs, whatever the goods.</summary>
    public static readonly IReadOnlyList<ShipmentDocumentKind> RequiredDocuments =
        [ShipmentDocumentKind.CommercialInvoice, ShipmentDocumentKind.PackingList, ShipmentDocumentKind.CertificateOfOrigin];

    public Guid TenantId { get; set; }

    /// <summary>The shipper's reference: unique in the tenant, stored upper-case.</summary>
    public string Reference { get; private set; } = default!;

    public string OriginCountry { get; private set; } = default!;
    public string DestinationCountry { get; private set; } = default!;
    public string HsCode { get; private set; } = default!;
    public decimal DeclaredValue { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal WeightKg { get; private set; }
    public ShipmentStatus Status { get; private set; }

    private readonly List<ShipmentDocument> _documents = [];
    public IReadOnlyList<ShipmentDocument> Documents => _documents;

    private Shipment()
    {
    }

    public static Shipment Create(string reference, string originCountry, string destinationCountry, string hsCode,
        decimal declaredValue, string currency, decimal weightKg, IEnumerable<ShipmentDocumentInput>? documents)
    {
        var shipment = new Shipment
        {
            Id = Guid.CreateVersion7(),
            Reference = Check(NormalizeReference(reference), ReferencePattern(), ReferenceMaxLength, "reference"),
            OriginCountry = Check(Upper(originCountry), CountryPattern(), 2, "originCountry"),
            DestinationCountry = Check(Upper(destinationCountry), CountryPattern(), 2, "destinationCountry"),
            HsCode = Check((hsCode ?? string.Empty).Trim(), HsCodePattern(), HsCodeMaxLength, "hsCode"),
            Currency = Check(Upper(currency), CurrencyPattern(), 3, "currency"),
            Status = ShipmentStatus.Draft
        };
        if (shipment.OriginCountry == shipment.DestinationCountry)
            throw new InvalidShipmentException("destinationCountry");
        if (declaredValue <= 0)
            throw new InvalidShipmentException("declaredValue");
        if (weightKg <= 0)
            throw new InvalidShipmentException("weightKg");
        shipment.DeclaredValue = declaredValue;
        shipment.WeightKg = weightKg;

        foreach (var document in documents ?? [])
            shipment.AddDocument(document.Kind, document.Reference, document.Content);
        return shipment;
    }

    public void AddDocument(ShipmentDocumentKind kind, string reference, string? content)
    {
        if (Status != ShipmentStatus.Draft)
            throw new InvalidShipmentStateException("shipment_not_draft");
        if (_documents.Count >= MaxDocuments)
            throw new InvalidShipmentException("documents");
        _documents.Add(ShipmentDocument.Create(Id, kind, reference, content));
        BumpVersionOnSave();
    }

    /// <summary>The required documents the shipment does not have.</summary>
    public IReadOnlyList<ShipmentDocumentKind> MissingDocuments() =>
        RequiredDocuments.Where(kind => _documents.All(d => d.Kind != kind)).ToList();

    /// <summary>Hands the shipment to customs preparation; the preparation agent starts for <paramref name="requestedBy"/>.</summary>
    public void MarkReadyForClearance(Guid requestedBy)
    {
        if (Status != ShipmentStatus.Draft)
            throw new InvalidShipmentStateException("shipment_not_draft");
        Status = ShipmentStatus.ReadyForClearance;
        AddIntegrationEvent(new ShipmentReadyForClearanceIntegrationEvent { ShipmentId = Id, RequestedBy = requestedBy });
    }

    public void EnsureReadyForClearance()
    {
        if (Status != ShipmentStatus.ReadyForClearance)
            throw new InvalidShipmentStateException("shipment_not_ready");
    }

    /// <summary>The declaration was accepted by customs. Clearance needs every required document.</summary>
    public void MarkDeclarationSubmitted()
    {
        EnsureReadyForClearance();
        if (MissingDocuments().Count > 0)
            throw new InvalidShipmentStateException("documents_missing");
        Status = ShipmentStatus.DeclarationSubmitted;
    }

    public static string NormalizeReference(string? reference) => Upper(reference);

    private static string Upper(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Check(string value, Regex pattern, int maxLength, string field) =>
        value.Length > 0 && value.Length <= maxLength && pattern.IsMatch(value) ? value : throw new InvalidShipmentException(field);

    [GeneratedRegex("^[A-Z0-9][A-Z0-9/_-]*$")]
    internal static partial Regex ReferencePattern();

    [GeneratedRegex("^[A-Z]{2}$")]
    internal static partial Regex CountryPattern();

    [GeneratedRegex("^[A-Z]{3}$")]
    internal static partial Regex CurrencyPattern();

    /// <summary><c>8471</c>, <c>8471.30</c>, <c>8471.30.00</c> or the digits without dots.</summary>
    [GeneratedRegex(@"^\d{4}(\.?\d{2}){0,3}$")]
    internal static partial Regex HsCodePattern();
}

/// <summary>A document of a shipment. Its content is supplied text the platform does not control.</summary>
public class ShipmentDocument : Entity<Guid>, IMustHaveTenant
{
    public const int ReferenceMaxLength = 100;
    public const int ContentMaxLength = 20_000;

    public Guid TenantId { get; set; }
    public Guid ShipmentId { get; private set; }
    public ShipmentDocumentKind Kind { get; private set; }
    public string Reference { get; private set; } = default!;
    public string? Content { get; private set; }

    private ShipmentDocument()
    {
    }

    internal static ShipmentDocument Create(Guid shipmentId, ShipmentDocumentKind kind, string reference, string? content)
    {
        var trimmed = (reference ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > ReferenceMaxLength || !Enum.IsDefined(kind))
            throw new InvalidShipmentException("documents");
        if (content is { Length: > ContentMaxLength })
            throw new InvalidShipmentException("documents");
        return new ShipmentDocument
        {
            Id = Guid.CreateVersion7(),
            ShipmentId = shipmentId,
            Kind = kind,
            Reference = trimmed,
            Content = string.IsNullOrWhiteSpace(content) ? null : content.Trim()
        };
    }
}
