namespace DataTransfer.Domains.TransferJobs.Events;

/// <summary>A transfer job reached a terminal status (completed, with errors, failed or cancelled). Same-module reaction.</summary>
public sealed record TransferJobFinishedEvent(
    Guid JobId,
    TransferJobKind Kind,
    string DefinitionKey,
    TransferJobStatus Status,
    Guid RequestedBy,
    string Culture) : IDomainEvent;
