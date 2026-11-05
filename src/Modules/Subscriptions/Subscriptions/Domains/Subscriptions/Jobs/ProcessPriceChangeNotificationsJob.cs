namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessPriceChangeNotificationsJob(
    IUnitOfWorkRunner unitOfWork,
    ILogger<ProcessPriceChangeNotificationsJob> logger)
{
    public Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return unitOfWork.RunAsync((services, _) => ExecuteAsync(services, cancellationToken), CancellationToken.None);
    }

    private async Task ExecuteAsync(IServiceProvider services, IJobCancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var notificationSender = services.GetRequiredService<INotificationSender>();
        var subscriberInfoProvider = services.GetRequiredService<ISubscriberInfoProvider>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var now = DateTimeOffset.UtcNow;
        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";

        // Find plans with an active price change announcement
        var plans = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.PriceChangeAnnouncedAt != null
                        && p.PendingPrice != null
                        && p.PriceEffectiveAt != null
                        && p.PriceEffectiveAt > now)
            .Select(p => new
            {
                p.Id,
                Name = p.Name.En,
                p.Price,
                p.Currency,
                PendingPrice = p.PendingPrice!.Value,
                PriceEffectiveAt = p.PriceEffectiveAt!.Value,
                AnnouncedAt = p.PriceChangeAnnouncedAt!.Value
            })
            .ToListAsync(CancellationToken.None);

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var daysSinceAnnouncement = (now - plan.AnnouncedAt).TotalDays;
            var daysUntilEffective = (int)Math.Ceiling((plan.PriceEffectiveAt - now).TotalDays);

            // Send on Day 0 (within first 24 h of announcement), Day 30, or Day 53 (7 days before effective)
            var shouldNotify = daysSinceAnnouncement is >= 0 and < 1
                            || daysSinceAnnouncement is >= 30 and < 31
                            || daysUntilEffective is >= 7 and <= 8;

            if (!shouldNotify)
                continue;

            // Find all active subscribers on this plan
            var subscriberIds = await dbContext.Subscriptions
                .AsNoTracking()
                .Where(s => s.PlanId == plan.Id &&
                            (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.Trialing))
                .Select(s => s.SubscriberId)
                .Distinct()
                .ToListAsync(CancellationToken.None);

            foreach (var subscriberId in subscriberIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var subscriber = await subscriberInfoProvider.GetAsync(subscriberId, CancellationToken.None);

                    var model = new PriceChangeNotificationModel
                    {
                        SubscriberName = subscriber?.DisplayName ?? string.Empty,
                        PlanName = plan.Name,
                        OldPrice = plan.Price.ToString("F2"),
                        NewPrice = plan.PendingPrice.ToString("F2"),
                        Currency = plan.Currency,
                        EffectiveAt = plan.PriceEffectiveAt.ToString("yyyy-MM-dd"),
                        DaysUntilEffective = daysUntilEffective.ToString(),
                        PortalUrl = $"{frontendBaseUrl}/subscription"
                    };

                    await notificationSender
                        .For(subscriberId)
                        .WithEmail(EmailTemplate.PriceChangeNotification, model.ToDictionary())
                        .SendAsync(forcedChannel: NotificationChannel.Email, ct: CancellationToken.None);

                    logger.LogInformation(
                        "Sent price-change notification to subscriber {SubscriberId} for plan {PlanName}. DaysUntilEffective={Days}",
                        subscriberId, plan.Name, daysUntilEffective);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error sending price-change notification to subscriber {SubscriberId} for plan {PlanId}.",
                        subscriberId, plan.Id);
                }
            }
        }
    }
}
