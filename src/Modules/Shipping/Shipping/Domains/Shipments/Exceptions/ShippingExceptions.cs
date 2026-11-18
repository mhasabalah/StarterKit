namespace Shipping.Domains.Shipments.Exceptions;

// Messages come from ShippingResource (en/ar) through ShippingText, in the current UI culture.

public sealed class ShipmentNotFoundException(Guid id)
    : NotFoundException(ShippingText.Get("Shipment.NotFound", id));

public sealed class ShipmentAlreadyExistsException : ConflictException
{
    public ShipmentAlreadyExistsException(string reference)
        : base(ShippingText.Get("Shipment.AlreadyExists", reference)) => ErrorCode = "shipment_already_exists";
}

/// <summary>A field of a shipment is not valid (400 <c>invalid_shipment</c>).</summary>
public sealed class InvalidShipmentException : BadRequestException
{
    public InvalidShipmentException(string field)
        : base(ShippingText.Get("Shipment.Invalid", field)) => ErrorCode = "invalid_shipment";
}

/// <summary>
/// The shipment's state does not allow the operation (409): <c>shipment_not_draft</c>, <c>shipment_not_ready</c>,
/// <c>documents_missing</c>, <c>declaration_already_submitted</c>.
/// </summary>
public sealed class InvalidShipmentStateException : ConflictException
{
    public InvalidShipmentStateException(string code)
        : base(ShippingText.Get("Shipment.State." + code)) => ErrorCode = code;
}

public sealed class DeclarationNotFoundException(Guid id)
    : NotFoundException(ShippingText.Get("Declaration.NotFound", id));
