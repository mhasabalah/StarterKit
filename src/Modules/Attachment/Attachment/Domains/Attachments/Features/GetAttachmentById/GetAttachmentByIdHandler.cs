using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Storage;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.GetAttachmentById;

public class GetAttachmentByIdQueryValidator : AbstractValidator<GetAttachmentByIdQuery>
{
    public GetAttachmentByIdQueryValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage(_ => localizer["Validation.AttachmentId.Required"]);
    }
}

/// <summary>Metadata of one attachment: public, admin, uploader, or entity policy CanRead.</summary>
public class GetAttachmentByIdHandler(
    IAttachmentRepository repository,
    IAttachmentAccessService accessService,
    IAttachmentUrls urls,
    ILogger<GetAttachmentByIdHandler> logger)
    : IQueryHandler<GetAttachmentByIdQuery, GetAttachmentByIdResult>
{
    public async Task<GetAttachmentByIdResult> Handle(GetAttachmentByIdQuery query, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetReadOnlyByIdAsync(query.Id, cancellationToken)
                         ?? throw new AttachmentNotFoundException(query.Id);

        if (!await accessService.CanReadAsync(attachment, cancellationToken))
        {
            logger.LogWarning("User {UserId} denied access to attachment {AttachmentId}",
                accessService.Current.UserId, query.Id);

            if (!accessService.Current.IsAuthenticated)
                throw new UnauthorizedException("Authentication is required to access this attachment.");

            throw new ForbiddenException("You do not have permission to access this attachment.");
        }

        return new GetAttachmentByIdResult(urls.ToDto(attachment));
    }
}
