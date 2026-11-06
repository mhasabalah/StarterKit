using MassTransit;
using Shared.Contracts.Messaging;

namespace DataTransfer.Contracts.IntegrationEvents;

/// <summary>
/// Integration event: a COMMIT import finished (completed, with or without row errors). Consumers that keep derived
/// data (search indexes, caches, analytics) can refresh what the definition touched. Dry runs, failed and cancelled
/// jobs raise nothing. Ids and counts only; the tenant travels as a header. The urn never changes.
/// </summary>
[MessageUrn("datatransfer.import-completed.v1")]
public sealed class DataImportCompletedIntegrationEvent : IntegrationEvent
{
    public Guid JobId { get; init; }
    public string DefinitionKey { get; init; } = default!;
    public Guid RequestedBy { get; init; }
    public long CreatedRows { get; init; }
    public long UpdatedRows { get; init; }
    public long FailedRows { get; init; }
}
