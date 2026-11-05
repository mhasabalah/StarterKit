using Identity.Data.Repository.RefreshTokens;
using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Sessions.Features.ActiveSessionIds;

/// <summary>
/// Sessions (refresh-token families) of the user with a live, unrevoked token. The refresh-token store is
/// authoritative (a revoked or expired family is gone here immediately, Redis blacklist or not).
/// </summary>
public sealed class GetActiveSessionIdsHandler(IRefreshTokenRepository refreshTokens, TimeProvider timeProvider)
    : IQueryHandler<GetActiveSessionIdsQuery, IReadOnlyCollection<Guid>>
{
    public async Task<IReadOnlyCollection<Guid>> Handle(GetActiveSessionIdsQuery query, CancellationToken cancellationToken)
    {
        if (query.UserId == Guid.Empty)
            return [];

        var active = await refreshTokens.ListActiveFamiliesAsync(
            query.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return active.Select(s => s.FamilyId).ToHashSet();
    }
}
