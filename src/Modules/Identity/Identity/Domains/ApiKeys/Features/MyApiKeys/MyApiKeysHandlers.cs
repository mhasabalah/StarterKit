using Identity.Contracts.ApiKeys;
using Identity.Data.Repository.ApiKeys;
using Identity.Domains.ApiKeys.Exceptions;
using Identity.Models;
using Identity.Options;
using Identity.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;

namespace Identity.Domains.ApiKeys.Features.MyApiKeys;

/// <summary>Every permission name a key may be scoped to (platform + tenant permissions).</summary>
public static class ApiKeyScopes
{
    public static IReadOnlySet<string> Grantable { get; } =
        Permissions.All.Concat(TenantPermissions.All).ToHashSet(StringComparer.Ordinal);
}

public class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    public const int MaxScopes = 50;

    public CreateApiKeyCommandValidator(IStringLocalizer<IdentityResource> localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => localizer["ApiKey.NameRequired"])
            .MaximumLength(ApiKey.MaxNameLength).WithMessage(_ => localizer["ApiKey.NameTooLong", ApiKey.MaxNameLength]);

        RuleFor(x => x.Scopes)
            .NotNull().WithMessage(_ => localizer["ApiKey.ScopesRequired"])
            .Must(s => s is { Count: > 0 }).WithMessage(_ => localizer["ApiKey.ScopesRequired"])
            .Must(s => s is null || s.Count <= MaxScopes).WithMessage(_ => localizer["ApiKey.TooManyScopes", MaxScopes]);

        RuleForEach(x => x.Scopes)
            .Must(s => s is not null && ApiKeyScopes.Grantable.Contains(s))
            .WithMessage((_, scope) => localizer["ApiKey.UnknownScope", scope]);

        RuleFor(x => x.ExpiresInDays)
            .GreaterThan(0).When(x => x.ExpiresInDays.HasValue)
            .WithMessage(_ => localizer["ApiKey.InvalidExpiry"]);
    }
}

public class CreateApiKeyHandler(
    IApiKeyRepository keys,
    IHttpContextService httpContextService,
    IOptions<ApiKeyOptions> options,
    TimeProvider timeProvider,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<CreateApiKeyHandler> logger,
    ITenantContext? tenantContext = null) : ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult>
{
    public async Task<CreateApiKeyResult> Handle(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new ForbiddenException(localizer["ApiKey.Disabled"]);

        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var days = command.ExpiresInDays ?? settings.DefaultExpiryDays;
        if (days > settings.MaxExpiryDays)
            throw new BadRequestException(localizer["ApiKey.ExpiryTooLong", settings.MaxExpiryDays]);

        EnsureCallerHoldsScopes(command.Scopes);

        if (await keys.CountActiveAsync(userId, now, cancellationToken) >= settings.MaxActiveKeysPerUser)
            throw new ConflictException(localizer["ApiKey.TooManyKeys", settings.MaxActiveKeysPerUser]);

        var (key, rawKey) = ApiKey.Create(
            userId, tenantContext?.TenantId, command.Name, command.Scopes, now, now.AddDays(days));
        await keys.AddAsync(key, cancellationToken);

        logger.LogInformation("API key {KeyId} ({Prefix}) created for user {UserId}", key.Id, key.Prefix, userId);
        return new CreateApiKeyResult(ApiKeyMapping.ToDto(key, now), rawKey);
    }

    /// <summary>
    /// A key can never exceed its owner, so asking for a permission the owner does not hold is refused up front
    /// (otherwise the scope would silently start working the day the owner is promoted).
    /// </summary>
    private void EnsureCallerHoldsScopes(IEnumerable<string> scopes)
    {
        var platform = RolePermissionMap.For(httpContextService.GetRoles()).ToHashSet(StringComparer.Ordinal);

        foreach (var scope in scopes)
        {
            var held = platform.Contains(scope) ||
                       (TenantPermissions.All.Contains(scope) &&
                        tenantContext?.Membership is { } membership && membership.HasPermission(scope));

            if (!held)
                throw new ForbiddenException(localizer["ApiKey.ScopeNotHeld", scope]);
        }
    }
}

public class GetMyApiKeysHandler(
    ApplicationIdentityDbContext dbContext,
    IHttpContextService httpContextService,
    TimeProvider timeProvider) : IQueryHandler<GetMyApiKeysQuery, List<ApiKeyDto>>
{
    public async Task<List<ApiKeyDto>> Handle(GetMyApiKeysQuery query, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var rows = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(k => ApiKeyMapping.ToDto(k, now)).ToList();
    }
}

public class RevokeMyApiKeyCommandValidator : AbstractValidator<RevokeMyApiKeyCommand>
{
    public RevokeMyApiKeyCommandValidator() => RuleFor(x => x.KeyId).NotEmpty();
}

public class RevokeMyApiKeyHandler(
    IApiKeyRepository keys,
    IHttpContextService httpContextService,
    TimeProvider timeProvider) : ICommandHandler<RevokeMyApiKeyCommand, RevokeApiKeyResult>
{
    public async Task<RevokeApiKeyResult> Handle(RevokeMyApiKeyCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Not authenticated");

        // Scoped to the owner: someone else's key id is indistinguishable from an unknown one.
        var key = await keys.FindOwnedAsync(userId, command.KeyId, cancellationToken)
                  ?? throw new ApiKeyNotFoundException(command.KeyId);

        var revoked = key.Revoke(timeProvider.GetUtcNow().UtcDateTime, userId);
        if (revoked)
            await keys.SaveAsync(cancellationToken);

        return new RevokeApiKeyResult(revoked);
    }
}

internal static class ApiKeyMapping
{
    public static ApiKeyDto ToDto(ApiKey k, DateTime nowUtc, Guid? includeUserId = null) => new(
        k.Id, k.Name, $"{ApiKey.KeyMarker}_{k.Prefix}", [.. k.Scopes], k.TenantId, k.CreatedAt, k.ExpiresAt,
        k.LastUsedAt, k.RevokedAt, k.IsActive(nowUtc), includeUserId);
}
