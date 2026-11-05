using Attachment.Contracts.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;

namespace Attachment.Infrastructure.Authorization;

/// <summary>
/// Single place that answers "may the current caller do X with this attachment / entity?".
/// Entity rules come from <see cref="IAttachmentEntityAccessPolicy"/> registrations (keyed by
/// <see cref="EntityType"/>); a missing policy means deny. Admins are always allowed.
/// </summary>
public interface IAttachmentAccessService
{
    AttachmentAccessContext Current { get; }

    Task<bool> CanAttachToEntityAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<bool> CanReadEntityAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Public file, admin, uploader, or the entity policy allows reading.</summary>
    Task<bool> CanReadAsync(AttachmentFile attachment, CancellationToken cancellationToken = default);

    /// <summary>See <c>DeleteAttachmentCommand</c> for the rules.</summary>
    Task<bool> CanDeleteAsync(AttachmentFile attachment, bool enforceEntityPolicy, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="ForbiddenException"/> (or <see cref="UnauthorizedException"/> when anonymous).</summary>
    Task EnsureCanAttachToEntityAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);
}

public sealed class AttachmentAccessService(
    IServiceProvider serviceProvider,
    IHttpContextService httpContextService,
    ILogger<AttachmentAccessService> logger) : IAttachmentAccessService
{
    private AttachmentAccessContext? _current;

    // Scoped service = one request. Policies usually load the owning entity (e.g. the product), so each
    // (operation, entity) decision is made once per request however often handlers ask.
    private readonly Dictionary<(bool Attach, EntityType Type, Guid Id), bool> _decisions = new();

    public AttachmentAccessContext Current => _current ??= BuildContext();

    public async Task<bool> CanAttachToEntityAsync(
        EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var caller = Current;
        if (caller.IsAdmin)
            return true;
        if (!caller.IsAuthenticated)
            return false;

        return await DecideAsync(attach: true, entityType, entityId,
            policy => policy.CanAttachAsync(entityId, caller, cancellationToken));
    }

    public async Task<bool> CanReadEntityAsync(
        EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var caller = Current;
        if (caller.IsAdmin)
            return true;

        return await DecideAsync(attach: false, entityType, entityId,
            policy => policy.CanReadAsync(entityId, caller, cancellationToken));
    }

    private async Task<bool> DecideAsync(
        bool attach, EntityType entityType, Guid entityId, Func<IAttachmentEntityAccessPolicy, Task<bool>> ask)
    {
        if (_decisions.TryGetValue((attach, entityType, entityId), out var known))
            return known;

        var policy = ResolvePolicy(entityType);
        var allowed = policy is not null && await ask(policy);
        _decisions[(attach, entityType, entityId)] = allowed;
        return allowed;
    }

    public async Task<bool> CanReadAsync(AttachmentFile attachment, CancellationToken cancellationToken = default)
    {
        if (!attachment.IsReady)
            return false; // uploading / pending deletion: never readable
        if (attachment.IsPublic)
            return true;

        var caller = Current;
        if (caller.IsAdmin)
            return true;
        if (caller.UserId.HasValue && attachment.UploadedBy == caller.UserId.Value)
            return true;

        return attachment is { EntityType: { } type, EntityId: { } id }
               && await CanReadEntityAsync(type, id, cancellationToken);
    }

    public async Task<bool> CanDeleteAsync(
        AttachmentFile attachment, bool enforceEntityPolicy, CancellationToken cancellationToken = default)
    {
        var caller = Current;
        if (caller.IsAdmin)
            return true;
        if (!caller.UserId.HasValue)
            return false;

        if (enforceEntityPolicy && attachment is { EntityType: { } type, EntityId: { } id })
            return await CanAttachToEntityAsync(type, id, cancellationToken);

        return attachment.UploadedBy == caller.UserId.Value;
    }

    public async Task EnsureCanAttachToEntityAsync(
        EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (await CanAttachToEntityAsync(entityType, entityId, cancellationToken))
            return;

        logger.LogWarning(
            "User {UserId} denied attaching files to {EntityType} {EntityId}",
            Current.UserId, entityType, entityId);

        if (!Current.IsAuthenticated)
            throw new UnauthorizedException("Authentication is required to attach files.");

        throw new ForbiddenException($"You are not allowed to attach files to this {entityType}.");
    }

    private IAttachmentEntityAccessPolicy? ResolvePolicy(EntityType entityType)
    {
        var policy = serviceProvider.GetKeyedService<IAttachmentEntityAccessPolicy>(entityType);
        if (policy is null)
        {
            logger.LogDebug("No attachment access policy registered for {EntityType}; denying", entityType);
        }

        return policy;
    }

    private AttachmentAccessContext BuildContext()
    {
        var userId = httpContextService.GetUserIdAsGuid();
        if (!userId.HasValue)
            return AttachmentAccessContext.Anonymous;

        var roles = httpContextService.GetRoles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isAdmin = roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.SuperAdmin);
        return new AttachmentAccessContext(userId, isAdmin, roles);
    }
}
