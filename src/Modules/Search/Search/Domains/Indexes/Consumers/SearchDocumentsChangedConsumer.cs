using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Search.Contracts.Indexing.IntegrationEvents;
using Search.Domains.Indexes.Features.ApplySearchDocumentChanges;
using Search.Observability;

namespace Search.Domains.Indexes.Consumers;

/// <summary>
/// Keeps an index in line with the owner's data: re-reads the changed documents from the source in the event's tenant
/// and upserts or removes them. Idempotent and order-independent (it never uses the event's content beyond the ids).
/// An unknown index is skipped (a definition was removed); anything else throws, so the bus retries.
/// </summary>
public sealed class SearchDocumentsChangedConsumer(
    IUnitOfWorkRunner unitOfWork,
    SearchIndexRegistry registry,
    SearchMetrics metrics,
    TimeProvider time,
    ILogger<SearchDocumentsChangedConsumer> logger) : IConsumer<SearchDocumentsChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<SearchDocumentsChangedIntegrationEvent> context)
    {
        var message = context.Message;
        if (registry.Find(message.IndexKey) is null)
        {
            logger.LogWarning("Search change for unknown index {IndexKey} skipped ({Count} documents)",
                message.IndexKey, message.DocumentIds.Count);
            return;
        }

        await unitOfWork.RunAsync(
            (services, ct) => services.GetRequiredService<ISender>()
                .Send(new ApplySearchDocumentChangesCommand(message.IndexKey, message.DocumentIds), ct),
            context.CancellationToken);

        metrics.Lag(message.IndexKey, time.GetUtcNow().UtcDateTime - message.OccurredOn);
    }
}
