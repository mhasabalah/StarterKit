using Agentic.Contracts.Knowledge;
using Agentic.Domains.Knowledge.Models;
using Agentic.Runtime.Knowledge;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Agentic.Domains.Knowledge.Features;

public class IndexKnowledgeDocumentCommandValidator : AbstractValidator<IndexKnowledgeDocumentCommand>
{
    public IndexKnowledgeDocumentCommandValidator(IStringLocalizer<AgenticResource> localizer, IOptions<AgenticOptions> options)
    {
        var maxText = options.Value.Knowledge.MaxTextLength;

        RuleFor(x => x.SourceKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.Knowledge.SourceKey.Required"])
            .MaximumLength(KnowledgeDocument.SourceKeyMaxLength).WithMessage(_ => localizer["Validation.Knowledge.SourceKey.MaxLength"]);
        RuleFor(x => x.ExternalRef)
            .NotEmpty().WithMessage(_ => localizer["Validation.Knowledge.ExternalRef.Required"])
            .MaximumLength(KnowledgeDocument.ExternalRefMaxLength).WithMessage(_ => localizer["Validation.Knowledge.ExternalRef.MaxLength"]);
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage(_ => localizer["Validation.Knowledge.Title.Required"])
            .MaximumLength(KnowledgeDocument.TitleMaxLength).WithMessage(_ => localizer["Validation.Knowledge.Title.MaxLength"]);
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.Text) != (x.AttachmentId is null))
            .WithName(nameof(IndexKnowledgeDocumentCommand.Text))
            .WithMessage(_ => localizer["Validation.Knowledge.Content.OneOf"]);
        RuleFor(x => x.Text)
            .MaximumLength(maxText).WithMessage(_ => localizer["Validation.Knowledge.Text.MaxLength", maxText])
            .When(x => x.Text is not null);
        RuleFor(x => x.AttachmentId).NotEqual(Guid.Empty).WithMessage(_ => localizer["Validation.Id.Required"])
            .When(x => x.AttachmentId is not null);
    }
}

/// <summary>
/// Makes a document searchable or replaces it (trusted; sent by the module that owns the source). The same content
/// sent again changes nothing; new content is a new revision, indexed in the background by
/// <see cref="Jobs.IndexKnowledgeDocumentJob"/>. While knowledge is switched off the command does nothing, so owning
/// modules can always send it.
/// </summary>
public class IndexKnowledgeDocumentHandler(
    IKnowledgeDocumentRepository repository,
    KnowledgeSourceRegistry sources,
    IKnowledgeEmbedder embedder,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<IndexKnowledgeDocumentHandler> logger) : ICommandHandler<IndexKnowledgeDocumentCommand, IndexKnowledgeDocumentResult>
{
    public async Task<IndexKnowledgeDocumentResult> Handle(IndexKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Knowledge.Enabled)
            return new IndexKnowledgeDocumentResult(null, 0, false);
        if (sources.Find(command.SourceKey) is null)
            throw new KnowledgeSourceNotFoundException(command.SourceKey);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var text = string.IsNullOrWhiteSpace(command.Text) ? null : command.Text;
        var title = command.Title.Trim();

        var document = await repository.FindAsync(command.SourceKey, command.ExternalRef, cancellationToken);
        bool queued;
        if (document is null)
        {
            document = KnowledgeDocument.Create(command.SourceKey, command.ExternalRef, title, text, command.AttachmentId, now);
            repository.Add(document);
            queued = true;
        }
        else
        {
            queued = document.Replace(title, text, command.AttachmentId, embedder.ModelIdentity);
        }

        if (queued)
            document.MarkEnqueued(now);
        await repository.SaveChangesAsync(cancellationToken);

        if (queued)
        {
            // After the commit: a lost enqueue is picked up by RequeueStalledKnowledgeDocumentsJob.
            KnowledgeQueue.Enqueue(jobs, document.Id, document.Revision);
            logger.LogInformation("Knowledge document {DocumentId} ({Source}) revision {Revision} queued for indexing",
                document.Id, document.SourceKey, document.Revision);
        }

        return new IndexKnowledgeDocumentResult(document.Id, document.Revision, queued);
    }
}

public class RemoveKnowledgeDocumentCommandValidator : AbstractValidator<RemoveKnowledgeDocumentCommand>
{
    public RemoveKnowledgeDocumentCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.SourceKey).NotEmpty().WithMessage(_ => localizer["Validation.Knowledge.SourceKey.Required"]);
        RuleFor(x => x.ExternalRef).NotEmpty().WithMessage(_ => localizer["Validation.Knowledge.ExternalRef.Required"]);
    }
}

/// <summary>Removes a document and its passages at once (trusted, owning module). Removing what is not there is a no-op.</summary>
public class RemoveKnowledgeDocumentHandler(IKnowledgeDocumentRepository repository)
    : ICommandHandler<RemoveKnowledgeDocumentCommand, bool>
{
    public async Task<bool> Handle(RemoveKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await repository.FindAsync(command.SourceKey, command.ExternalRef, cancellationToken);
        if (document is null)
            return false;

        repository.Remove(document);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class GetKnowledgeDocumentsQueryValidator : AbstractValidator<GetKnowledgeDocumentsQuery>
{
    public GetKnowledgeDocumentsQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"]);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"]);
        RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"]).When(x => x.Status.HasValue);
    }
}

/// <summary>The tenant's indexed documents for operators (the endpoint requires <c>agentic.manage</c>); no content.</summary>
public class GetKnowledgeDocumentsHandler(AgenticDbContext dbContext)
    : IQueryHandler<GetKnowledgeDocumentsQuery, PaginatedResult<KnowledgeDocumentDto>>
{
    public async Task<PaginatedResult<KnowledgeDocumentDto>> Handle(GetKnowledgeDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        var documents = dbContext.KnowledgeDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.SourceKey))
            documents = documents.Where(d => d.SourceKey == query.SourceKey);
        if (query.Status is { } status)
            documents = documents.Where(d => d.Status == status);

        return await documents
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new KnowledgeDocumentDto(d.Id, d.SourceKey, d.ExternalRef, d.Title, d.AttachmentId, d.Status,
                d.Revision, d.ChunkCount, d.EmbeddingModel, d.FailureCode, d.IndexedAt, d.CreatedAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
