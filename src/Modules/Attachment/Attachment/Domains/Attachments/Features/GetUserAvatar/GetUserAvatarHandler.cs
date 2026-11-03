using Attachment.Contracts.Attachments.Queries;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.Features.GetUserAvatar;

public class GetUserAvatarQueryValidator : AbstractValidator<GetUserAvatarQuery>
{
    public GetUserAvatarQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

/// <summary>
/// The user's current avatar: newest Ready <c>UserAvatar</c> attachment of the user (the lifecycle filter hides
/// Uploading / PendingDeletion rows, so a replaced avatar never wins). Runs in the ambient tenant scope.
/// </summary>
public class GetUserAvatarHandler(AttachmentDbContext dbContext, IAttachmentUrls urls)
    : IQueryHandler<GetUserAvatarQuery, GetUserAvatarResult>
{
    public async Task<GetUserAvatarResult> Handle(GetUserAvatarQuery query, CancellationToken cancellationToken)
    {
        var avatarId = await dbContext.Attachments.AsNoTracking()
            .Where(a => a.EntityType == EntityType.User && a.EntityId == query.UserId &&
                        a.Category == AttachmentCategory.UserAvatar)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return avatarId is { } id
            ? new GetUserAvatarResult(id, urls.File(id))
            : new GetUserAvatarResult(null, null);
    }
}
