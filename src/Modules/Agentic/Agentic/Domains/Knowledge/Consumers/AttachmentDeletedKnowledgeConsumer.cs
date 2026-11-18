using Attachment.Contracts.Attachments.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Domains.Knowledge.Consumers;

/// <summary>
/// A deleted file must not stay searchable: removes the knowledge documents indexed from the attachment, with their
/// passages (the foreign key cascades), in the event's tenant. Idempotent: a redelivery finds nothing.
/// </summary>
public sealed class AttachmentDeletedKnowledgeConsumer(
    IUnitOfWorkRunner unitOfWork,
    ILogger<AttachmentDeletedKnowledgeConsumer> logger) : IConsumer<AttachmentDeletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AttachmentDeletedIntegrationEvent> context)
    {
        var attachmentId = context.Message.AttachmentId;
        var removed = await unitOfWork.RunAsync(
            (services, ct) => services.GetRequiredService<AgenticDbContext>().KnowledgeDocuments
                .Where(d => d.AttachmentId == attachmentId)
                .ExecuteDeleteAsync(ct),
            context.CancellationToken);

        if (removed > 0)
            logger.LogInformation("Attachment {AttachmentId} was deleted: {Count} knowledge document(s) removed", attachmentId, removed);
    }
}
