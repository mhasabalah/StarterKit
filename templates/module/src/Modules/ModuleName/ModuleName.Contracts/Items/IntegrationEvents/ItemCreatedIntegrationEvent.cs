using MassTransit;
using Shared.Contracts.Messaging;

namespace ModuleName.Contracts.Items.IntegrationEvents;

/// <summary>
/// Integration event: an item was created. Raised by the Item aggregate, written to the ModuleName outbox in the same
/// transaction, then delivered at least once. Other modules consume it with an <c>IConsumer&lt;T&gt;</c>.
/// Ids and a minimal projection only, never the aggregate. The urn is the stable wire name: never change it.
/// </summary>
[MessageUrn("modulename.item-created.v1")]
public sealed class ItemCreatedIntegrationEvent : IntegrationEvent
{
    public Guid ItemId { get; init; }
    public string Code { get; init; } = default!;
}
