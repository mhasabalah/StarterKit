using Shared.Contracts.Pagination;
using Shared.Core.Extensions;

namespace Shared.Email.Persistence;

public sealed class EmailLogRepository(EmailDbContext db)
{
    public async Task InsertAsync(EmailLog log, CancellationToken ct = default)
    {
        db.EmailLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(
        Guid id,
        EmailDeliveryStatus status,
        string? error,
        DateTime? sentAt,
        string? hangfireJobId,
        CancellationToken ct = default)
    {
        var query = db.EmailLogs.Where(x => x.Id == id);

        // Split into two paths to avoid un-translatable null-coalescing in the
        // SetProperty expression tree (EF Core 9 does not support x => a ?? x.Prop).
        if (hangfireJobId is not null)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentAt, sentAt)
                .SetProperty(x => x.HangfireJobId, hangfireJobId), ct);
        }
        else
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentAt, sentAt), ct);
        }
    }

    public async Task<EmailLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.EmailLogs.FindAsync([id], ct);

    // P1 + P4: paginated projection — message_json column is excluded to avoid loading large blobs.
    public async Task<PaginatedResult<FailedEmailDto>> GetFailedPagedAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
        => await db.EmailLogs
            .Where(x => x.Status == EmailDeliveryStatus.Failed
                     && x.CreatedAt > DateTime.UtcNow.AddHours(-24))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FailedEmailDto(
                x.Id,
                x.UserId,
                x.TemplateName,
                x.Recipient,
                x.Subject,
                x.Culture,
                x.Status,
                x.Error,
                x.CreatedAt,
                x.HangfireJobId))
            .PagedResult(page, pageSize, ct);

    // P3: cheap count for the daily summary job — no entity materialization.
    public async Task<int> CountFailedSince24HoursAsync(CancellationToken ct = default)
        => await db.EmailLogs
            .CountAsync(x => x.Status == EmailDeliveryStatus.Failed
                          && x.CreatedAt > DateTime.UtcNow.AddHours(-24), ct);
}
