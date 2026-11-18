using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using MassTransit;
using Shipping.Contracts.Shipments.IntegrationEvents;
using Shipping.Domains.Agents;

namespace Shipping.Domains.Shipments.Consumers;

/// <summary>
/// The event trigger of the proof of concept: a shipment handed to customs preparation starts the preparation agent
/// on behalf of the user who handed it over (no system identity). The trigger key makes a redelivered event return
/// the run that already exists. "Nothing to do" (agents switched off, the user may not start the agent) is logged and
/// acknowledged; a rate limit or an outage is thrown, so the bus retries.
/// </summary>
public sealed class ShipmentReadyForClearanceConsumer(ISender sender, ILogger<ShipmentReadyForClearanceConsumer> logger)
    : IConsumer<ShipmentReadyForClearanceIntegrationEvent>
{
    public async Task Consume(ConsumeContext<ShipmentReadyForClearanceIntegrationEvent> context)
    {
        var message = context.Message;
        try
        {
            var run = await sender.Send(new RequestAgentRunCommand(
                ShipmentPreparationAgent.Key,
                AgentTriggerKind.Event,
                $"shipment:{message.ShipmentId:N}:ready-for-clearance",
                message.RequestedBy,
                "Prepare the customs declaration of this shipment.",
                ShipmentPreparationAgent.InputFor(message.ShipmentId)), context.CancellationToken);

            logger.LogInformation("Shipment {ShipmentId}: preparation agent run {RunId} ({State})", message.ShipmentId,
                run.RunId, run.Created ? "started" : "already started");
        }
        catch (Exception ex) when (ex is NotFoundException or ForbiddenException)
        {
            logger.LogWarning("Shipment {ShipmentId}: the preparation agent was not started: {Reason}", message.ShipmentId,
                ex.Message);
        }
    }
}
