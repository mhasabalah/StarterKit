using Search.Contracts.Indexing.IntegrationEvents;
using Search.Domains.Indexes.Services;

namespace Search.Domains.Indexes.Features.ApplySearchDocumentChanges;

/// <summary>
/// Internal: re-index these documents of an index from their source (the outbox consumer and the drift repair send
/// it). Not in Contracts: owners raise <see cref="SearchDocumentsChangedIntegrationEvent"/> instead.
/// </summary>
public sealed record ApplySearchDocumentChangesCommand(string IndexKey, IReadOnlyList<string> DocumentIds) : ICommand<IndexChangeResult>;

public sealed class ApplySearchDocumentChangesCommandValidator : AbstractValidator<ApplySearchDocumentChangesCommand>
{
    public ApplySearchDocumentChangesCommandValidator(SearchIndexRegistry registry, IStringLocalizer<SearchResource> localizer)
    {
        RuleFor(x => x.IndexKey).Must(key => registry.Find(key) is not null)
            .WithMessage(x => localizer["Validation.IndexKey.Unknown", x.IndexKey]);
        RuleFor(x => x.DocumentIds).NotNull()
            .Must(ids => ids.Count <= SearchDocumentsChangedIntegrationEvent.MaxDocuments)
            .WithMessage(_ => localizer["Validation.Documents.Max", SearchDocumentsChangedIntegrationEvent.MaxDocuments]);
    }
}

public sealed class ApplySearchDocumentChangesHandler(SearchIndexRegistry registry, SearchIndexer indexer)
    : ICommandHandler<ApplySearchDocumentChangesCommand, IndexChangeResult>
{
    public Task<IndexChangeResult> Handle(ApplySearchDocumentChangesCommand command, CancellationToken cancellationToken) =>
        indexer.ApplyChangesAsync(registry.Find(command.IndexKey)!, command.DocumentIds, cancellationToken);
}
