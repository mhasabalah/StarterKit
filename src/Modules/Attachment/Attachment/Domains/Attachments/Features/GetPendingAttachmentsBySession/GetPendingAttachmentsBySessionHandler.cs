using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

public class GetPendingAttachmentsBySessionHandler(
    IPendingAttachmentRepository repository,
    IPendingSessionService sessionService,
    PendingAttachmentDtoFactory dtoFactory,
    IOptions<AttachmentOptions> options)
    : IQueryHandler<GetPendingAttachmentsBySessionQuery, GetPendingAttachmentsBySessionResult>
{
    public async Task<GetPendingAttachmentsBySessionResult> Handle(
        GetPendingAttachmentsBySessionQuery query,
        CancellationToken cancellationToken)
    {
        if (!sessionService.IsOwnedByCurrentCaller(query.SessionId))
            throw new PendingSessionAccessDeniedException();

        var pending = await repository.GetBySessionIdAsync(query.SessionId, cancellationToken);

        var dtos = new List<PendingAttachmentDto>(pending.Count);
        foreach (var p in pending)
            dtos.Add(await dtoFactory.CreateAsync(p, cancellationToken));

        var remaining = Math.Max(0, options.Value.Pending.MaxUploadsPerSession - dtos.Count);
        return new GetPendingAttachmentsBySessionResult(dtos, dtos.Count, remaining);
    }
}
