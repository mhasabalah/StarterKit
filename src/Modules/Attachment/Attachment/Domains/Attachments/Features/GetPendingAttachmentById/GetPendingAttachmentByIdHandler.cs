using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentById;

/// <summary>Trusted (module-internal) lookup; there is no HTTP endpoint for it.</summary>
public class GetPendingAttachmentByIdHandler(
    IPendingAttachmentRepository repository,
    PendingAttachmentDtoFactory dtoFactory)
    : IQueryHandler<GetPendingAttachmentByIdQuery, PendingAttachmentDto?>
{
    public async Task<PendingAttachmentDto?> Handle(GetPendingAttachmentByIdQuery query, CancellationToken cancellationToken)
    {
        var pending = await repository.GetByIdAsync(query.Id, cancellationToken);
        return pending is null ? null : await dtoFactory.CreateAsync(pending, cancellationToken);
    }
}
