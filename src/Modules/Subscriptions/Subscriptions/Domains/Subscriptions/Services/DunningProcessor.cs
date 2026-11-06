using Shared.Contracts.Localization;
using System.Globalization;
using Notification.Contracts.InApp;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.OnlinePayments.Services;
using Subscriptions.Resources;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Dunning: reminds a subscriber whose renewal is overdue (PastDue) to pay, each time with a FRESH checkout link.
/// Steps are <c>Subscriptions:Dunning:ScheduleDays</c> after the subscription went past due, plus a final notice
/// <c>FinalNoticeDaysBeforeGraceEnd</c> days before the grace period ends. Every step is recorded
/// (<see cref="DunningNotice"/>, unique per payment + step) BEFORE the email, so a step never re-sends, even when two
/// runs overlap. When a run finds several steps due (the job was down), only the latest is sent; the others are
/// recorded as superseded. A renewal whose proof is under review (Submitted) is not nagged.
/// With <c>Subscriptions:Dunning:AutoRetry:Enabled</c> each step first tries ONE off-session charge of the subscriber's
/// saved default card (<see cref="IOffSessionRetryService"/>, ADR 0018): success settles the payment and sends no
/// reminder; anything else sends the reminder with the reason, plus an in-app notice.
/// </summary>
public interface IDunningProcessor
{
    /// <returns>The number of notices sent.</returns>
    Task<int> ProcessAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class DunningProcessor(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    IConfiguration configuration,
    ILogger<DunningProcessor> logger)
    : IDunningProcessor
{
    private static readonly PaymentStatus[] Unpaid = [PaymentStatus.Pending, PaymentStatus.Rejected, PaymentStatus.PartiallyPaid];

    public async Task<int> ProcessAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!options.Value.Dunning.Enabled)
            return 0;

        var ids = await unitOfWork.RunAsync((services, token) =>
        {
            var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
            return dbContext.Subscriptions
                .AsNoTracking()
                .Where(x => x.Status == SubscriptionStatus.PastDue &&
                            x.GracePeriodEndsAt > now &&
                            dbContext.Payments.Any(p => p.SubscriptionId == x.Id &&
                                                        p.Purpose == PaymentPurpose.Renewal &&
                                                        Unpaid.Contains(p.Status)))
                .OrderBy(x => x.GracePeriodEndsAt)
                .Select(x => x.Id)
                .ToListAsync(token);
        }, ct);

        var sent = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await unitOfWork.RunAsync((services, token) => NotifyAsync(services, id, now, token), ct))
                    sent++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() || ex is ConflictException)
            {
                // Another run recorded the step first: it sends the notice.
                logger.LogWarning(ex, "Dunning for subscription {SubscriptionId} skipped: {Message}", id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dunning for subscription {SubscriptionId} failed.", id);
            }
        }

        return sent;
    }

    private async Task<bool> NotifyAsync(IServiceProvider services, Guid subscriptionId, DateTimeOffset now, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var settings = options.Value;

        var subscription = await dbContext.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is not { Status: SubscriptionStatus.PastDue, GracePeriodEndsAt: { } graceEnd } || graceEnd <= now)
            return false;

        var payment = await dbContext.Payments.AsNoTracking()
            .Where(p => p.SubscriptionId == subscriptionId && p.Purpose == PaymentPurpose.Renewal && Unpaid.Contains(p.Status))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (payment is null)
            return false;

        var recorded = await dbContext.DunningNotices.AsNoTracking()
            .Where(x => x.PaymentId == payment.Id)
            .Select(x => x.Step)
            .ToListAsync(ct);

        var due = DueSteps(payment.CreatedAt, graceEnd, now, settings.Dunning).Where(s => !recorded.Contains(s)).ToList();
        if (due.Count == 0)
            return false;

        // Only the latest due step is sent; earlier ones the job missed are recorded as superseded.
        var send = due[^1];
        foreach (var step in due)
            dbContext.DunningNotices.Add(DunningNotice.Record(subscriptionId, payment.Id, step, sent: step == send, now));
        await dbContext.SaveChangesAsync(ct); // a unique-index race surfaces as ConflictException: the other run sends

        // Automatic retry first: a collected payment needs no reminder (the settlement sends the invoice).
        var retry = await services.GetRequiredService<IOffSessionRetryService>()
            .TryChargeAsync(subscription, payment, send, now, ct);
        if (retry.Outcome == AutoRetryOutcome.Succeeded)
        {
            logger.LogInformation(
                "Dunning step {Step} collected payment {PaymentId} of subscription {SubscriptionId} off-session.",
                send, payment.Id, subscriptionId);
            return false;
        }

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var renewalUrl = $"{frontendBaseUrl}/subscription/renew";
        var paymentUrl = await TryCreateCheckoutAsync(services, subscription, payment, frontendBaseUrl, ct) ?? renewalUrl;

        var subscriber = await services.GetRequiredService<ISubscriberInfoProvider>().GetAsync(subscription.SubscriberId, ct);
        var model = new SubscriptionDunningModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = subscription.PlanName,
            AmountDue = CurrencyRules.FormatWithCode(payment.OutstandingAmount, payment.Currency),
            GraceDeadline = graceEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PaymentUrl = paymentUrl,
            IsFinal = send == DunningNotice.FinalStep,
            AutoRetryReason = retry.Reason ?? string.Empty
        };

        var deadline = model.GraceDeadline;
        var inApp = new InAppContent(
            Title: LocalizedText.Create(
                SubscriptionsText.En("Billing.PaymentFailed.Title"), SubscriptionsText.Ar("Billing.PaymentFailed.Title")),
            Body: LocalizedText.Create(
                SubscriptionsText.En("Billing.PaymentFailed.Body", model.AmountDue, subscription.PlanName, deadline),
                SubscriptionsText.Ar("Billing.PaymentFailed.Body", model.AmountDue, subscription.PlanName, deadline)),
            Category: "billing.payment-failed",
            ActionUrl: "/subscription/renew",
            Severity: NotificationSeverity.Warning,
            Data: new Dictionary<string, string>
            {
                ["subscriptionId"] = subscriptionId.ToString(),
                ["paymentId"] = payment.Id.ToString()
            });

        await services.GetRequiredService<INotificationSender>()
            .For(subscription.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionDunning, model.ToDictionary())
            .WithInApp(inApp)
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: ct);

        logger.LogInformation(
            "Dunning step {Step} sent for subscription {SubscriptionId} (payment {PaymentId}).", send, subscriptionId, payment.Id);
        return true;
    }

    /// <summary>
    /// Steps due at <paramref name="now"/>, oldest first, the final notice last. Schedule days that fall on or after the
    /// final notice are dropped (the final notice replaces them).
    /// </summary>
    public static IReadOnlyList<string> DueSteps(
        DateTimeOffset pastDueSince,
        DateTimeOffset graceEnd,
        DateTimeOffset now,
        DunningOptions dunning)
    {
        var finalAt = graceEnd.AddDays(-Math.Max(0, dunning.FinalNoticeDaysBeforeGraceEnd));
        var steps = new List<string>();

        foreach (var day in dunning.EffectiveScheduleDays.Where(d => d > 0).Distinct().Order())
        {
            var at = pastDueSince.AddDays(day);
            if (at < finalAt && at <= now)
                steps.Add(DunningNotice.StepKey(day));
        }

        if (finalAt <= now)
            steps.Add(DunningNotice.FinalStep);

        return steps;
    }

    // A fresh hosted checkout for the owed payment through the first enabled gateway; null when none works
    // (the email then links to the renewal page, where the subscriber can pay or upload a proof).
    private async Task<string?> TryCreateCheckoutAsync(
        IServiceProvider services,
        Subscription subscription,
        SubscriptionPayment payment,
        string frontendBaseUrl,
        CancellationToken ct)
    {
        var provider = options.Value.PaymentGateways
            .Where(g => g.Value.Enabled)
            .Select(g => g.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (provider is null)
            return null;

        try
        {
            var result = await services.GetRequiredService<ISender>().Send(new CreateOnlineCheckoutCommand(
                subscription.Id,
                subscription.SubscriberId,
                provider,
                $"{frontendBaseUrl}/subscription/renew?payment=success",
                $"{frontendBaseUrl}/subscription/renew?payment=cancelled",
                payment.Id), ct);
            return result.RedirectUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not create a checkout link for dunning of subscription {SubscriptionId}.", subscription.Id);
            return null;
        }
    }
}
