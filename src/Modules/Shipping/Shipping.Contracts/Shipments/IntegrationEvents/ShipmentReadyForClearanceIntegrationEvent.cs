using MassTransit;
using Shared.Contracts.Messaging;

namespace Shipping.Contracts.Shipments.IntegrationEvents;

/// <summary>
/// Integration event: a shipment was handed to customs preparation. Raised by the Shipment aggregate and written to
/// the Shipping outbox in the same transaction. Shipping's own consumer starts the preparation agent for
/// <see cref="RequestedBy"/>; other modules may react too. Ids only.
/// </summary>
[MessageUrn("shipping.shipment-ready-for-clearance.v1")]
public sealed class ShipmentReadyForClearanceIntegrationEvent : IntegrationEvent
{
    public Guid ShipmentId { get; init; }

    /// <summary>The user who marked it ready: the agent acts for them.</summary>
    public Guid RequestedBy { get; init; }
}
