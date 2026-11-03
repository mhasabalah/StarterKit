using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

/// <summary>
/// Module-to-module query: the CURRENT avatar of a user (newest Ready <c>UserAvatar</c> attachment) as its stable
/// download URL, or <c>null</c> when the user has none. No caller-based access check - it only returns the URL; the
/// download endpoint itself enforces access. Used by consumers of <c>UserAvatarChangedIntegrationEvent</c>.
/// </summary>
public record GetUserAvatarQuery(Guid UserId) : IQuery<GetUserAvatarResult>;

public record GetUserAvatarResult(Guid? AttachmentId, string? AvatarUrl);
