using System.Globalization;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Sends the "trial ending" email once per trial, <c>Subscriptions:Trials:ReminderDaysBeforeEnd</c> days before it ends.
/// Each subscription runs in its own unit of work; the reminder is marked sent in the SAME commit that precedes the
/// email, so a retry or an overlapping run never sends it twice.
/// </summary>
public interface ITrialReminderProcessor
{
    /// <returns>The number of reminders sent.</returns>
    Task<int> ProcessAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class TrialReminderProcessor(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    IConfiguration configuration,
    ILogger<TrialReminderProcessor> logger)
    : ITrialReminderProcessor
{
    public async Task<int> ProcessAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = options.Value;
        var horizon = now.AddDays(Math.Max(0, settings.Trials.ReminderDaysBeforeEnd));

        var ids = await unitOfWork.RunAsync((services, token) =>
            services.GetRequiredService<SubscriptionsDbContext>().Subscriptions
                .AsNoTracking()
                .Where(x => x.Status == SubscriptionStatus.Trialing &&
                            x.TrialReminderSentAt == null &&
                            x.TrialEndsAt > now &&
                            x.TrialEndsAt <= horizon)
                .OrderBy(x => x.TrialEndsAt)
                .Select(x => x.Id)
                .ToListAsync(token), ct);

        var sent = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await unitOfWork.RunAsync((services, token) => RemindAsync(services, id, now, token), ct))
                    sent++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() || ex is ConflictException)
            {
                logger.LogWarning(ex, "Trial reminder for subscription {SubscriptionId} skipped: {Message}", id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trial reminder for subscription {SubscriptionId} failed.", id);
            }
        }

        return sent;
    }

    private async Task<bool> RemindAsync(IServiceProvider services, Guid subscriptionId, DateTimeOffset now, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var repository = services.GetRequiredService<ISubscriptionRepository>();
        var settings = options.Value;

        var subscription = await repository.GetByIdAsync(subscriptionId, ct);
        if (subscription is not { Status: SubscriptionStatus.Trialing, TrialReminderSentAt: null, TrialEndsAt: { } endsAt })
            return false;

        var plan = await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
        var period = PricingPolicy.RenewalPeriodPrice(subscription, plan, endsAt);
        var gross = TaxCalculator.AmountDue(
            period - CouponPricing.Discount(period, subscription.ActiveCoupon), settings.Tax.ToPolicy());

        subscription.MarkTrialReminderSent(now);
        await dbContext.SaveChangesAsync(ct);

        var subscriber = await services.GetRequiredService<ISubscriberInfoProvider>().GetAsync(subscription.SubscriberId, ct);
        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new TrialEndingModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = subscription.PlanName,
            TrialEndDate = endsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DaysLeft = Math.Max(1, (int)Math.Ceiling((endsAt - now).TotalDays)),
            PriceAfterTrial = $"{gross.ToString("F2", CultureInfo.InvariantCulture)} {subscription.Currency}",
            SubscriptionUrl = $"{frontendBaseUrl}/subscription"
        };

        await services.GetRequiredService<INotificationSender>()
            .For(subscription.SubscriberId)
            .WithEmail(EmailTemplate.TrialEnding, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: ct);

        logger.LogInformation("Trial-ending reminder sent for subscription {SubscriptionId}.", subscription.Id);
        return true;
    }
}
