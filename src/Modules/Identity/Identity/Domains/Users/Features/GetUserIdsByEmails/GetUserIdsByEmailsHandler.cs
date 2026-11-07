using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUserIdsByEmails;

/// <summary><see cref="GetUserIdsByEmailsQuery"/>: one indexed query on the normalized e-mail column.</summary>
public sealed class GetUserIdsByEmailsHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUserIdsByEmailsQuery, IReadOnlyDictionary<string, Guid>>
{
    public async Task<IReadOnlyDictionary<string, Guid>> Handle(GetUserIdsByEmailsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Emails.Count > GetUserIdsByEmailsQuery.MaxEmails)
            throw new BadRequestException($"At most {GetUserIdsByEmailsQuery.MaxEmails} e-mails per request.");

        var normalized = query.Emails.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (normalized.Count == 0)
            return new Dictionary<string, Guid>();

        var rows = await dbContext.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail != null && normalized.Contains(u.NormalizedEmail))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        return rows.Where(r => r.Email is not null)
            .GroupBy(r => r.Email!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
    }
}
