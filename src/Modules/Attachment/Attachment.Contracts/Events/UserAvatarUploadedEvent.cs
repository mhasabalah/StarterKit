using Shared.Core.DDD;

namespace Attachment.Contracts.Events;

public record UserAvatarUploadedEvent(Guid UserId, string AvatarUrl) : IDomainEvent;
