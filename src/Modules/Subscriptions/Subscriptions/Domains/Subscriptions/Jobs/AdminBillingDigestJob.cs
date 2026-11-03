using System.Globalization;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>What the digest reported (returned for logs/tests).</summary>
public sealed record AdminBillingDigest(
    int PendingProofs,
    int StaleProofs,
    double? OldestPendingAgeHours,
    int PastDueCount,
    int PartiallyPaidCount,
    int RecipientCount);

/// <summary>
/// Daily billing digest (job id <c>admin-billing-digest</c>, cron <c>Subscriptions:AdminNotifications:DigestCron</c>)
/// to the admin notification recipients (<see cref="IAdminNotificationRecipients"/>): proofs awaiting review (and how
/// many are older than <c>DigestStaleProofHours</c>), the oldest proof age, past-due subscriptions and partially
/// paid payments. Nothing to report = no email. Runs through <see cref="IUnitOfWorkRunner"/> (fresh scope; events, if
/// any, published after success) like every background job.
/// </summary>
[DisableConcurrentExecution(60)]
public class AdminBillingDigestJob(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdminBillingDigestJob> logger)
{
    public async Task<AdminBillingDigest> ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var staleHours = Math.Max(1, options.Value.AdminNotifications.DigestStaleProofHours);

        var digest = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
            var staleBefore = now.AddHours(-staleHours);

            var submitted = dbContext.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Submitted);
            var pending = await submitted.CountAsync(ct);
            var stale = await submitted.CountAsync(p => (p.SubmittedAt ?? p.CreatedAt) < staleBefore, ct);
            DateTimeOffset? oldest = pending == 0
                ? null
                : await submitted.MinAsync(p => p.SubmittedAt ?? p.CreatedAt, ct);
            var pastDue = await dbContext.Subscriptions.AsNoTracking()
                .CountAsync(s => s.Status == SubscriptionStatus.PastDue, ct);
            var partiallyPaid = await dbContext.Payments.AsNoTracking()
                .CountAsync(p => p.Status == PaymentStatus.PartiallyPaid, ct);

            var oldestHours = oldest is { } at ? Math.Round(Math.Max(0d, (now - at).TotalHours), 1) : (double?)null;
            if (pending == 0 && pastDue == 0 && partiallyPaid == 0)
            {
                return new AdminBillingDigest(0, 0, null, 0, 0, 0);
            }

            var recipients = await services.GetRequiredService<IAdminNotificationRecipients>().GetEmailsAsync(ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning("Billing digest not sent: no admin notification recipients configured (Subscriptions:AdminNotifications).");
                return new AdminBillingDigest(pending, stale, oldestHours, pastDue, partiallyPaid, 0);
            }

            var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
            var model = new AdminBillingDigestModel
            {
                PendingProofs = pending.ToString(CultureInfo.InvariantCulture),
                StaleProofs = stale.ToString(CultureInfo.InvariantCulture),
                StaleThresholdHours = staleHours.ToString(CultureInfo.InvariantCulture),
                OldestPendingAge = oldestHours is { } h ? h.ToString("0.#", CultureInfo.InvariantCulture) + " h" : "-",
                PastDueCount = pastDue.ToString(CultureInfo.InvariantCulture),
                PartiallyPaidCount = partiallyPaid.ToString(CultureInfo.InvariantCulture),
                GeneratedAt = now.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                AdminDashboardUrl = $"{frontendBaseUrl}/admin/billing"
            };

            var sender = services.GetRequiredService<INotificationSender>();
            foreach (var email in recipients)
            {
                await sender
                    .ToEmail(email)
                    .WithEmail(EmailTemplate.AdminBillingDigest, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, ct: ct);
            }

            return new AdminBillingDigest(pending, stale, oldestHours, pastDue, partiallyPaid, recipients.Count);
        }, CancellationToken.None);

        logger.LogInformation(
            "Billing digest: {Pending} proofs pending ({Stale} older than {Hours} h), {PastDue} past due, {Partial} partially paid, sent to {Recipients} recipient(s).",
            digest.PendingProofs, digest.StaleProofs, staleHours, digest.PastDueCount, digest.PartiallyPaidCount, digest.RecipientCount);

        return digest;
    }
}
