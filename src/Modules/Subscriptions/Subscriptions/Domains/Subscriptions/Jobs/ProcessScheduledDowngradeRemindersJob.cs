namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessScheduledDowngradeRemindersJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessScheduledDowngradeRemindersJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var notificationSender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var subscriberInfoProvider = scope.ServiceProvider.GetRequiredService<ISubscriberInfoProvider>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(7);
        var windowEnd = now.AddDays(8);

        var pendingChanges = await dbContext.ScheduledPlanChanges
            .AsNoTracking()
            .Where(x => x.Status == PlanChangeStatus.Pending
                        && x.EffectiveAt >= windowStart
                        && x.EffectiveAt < windowEnd)
            .Join(dbContext.Subscriptions,
                c => c.SubscriptionId,
                s => s.Id,
                (c, s) => new
                {
                    s.SubscriberId,
                    CurrentPlanName = s.PlanName,
                    c.NewPlanName,
                    c.EffectiveAt
                })
            .ToListAsync(CancellationToken.None);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";

        foreach (var change in pendingChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var subscriber = await subscriberInfoProvider.GetAsync(change.SubscriberId, CancellationToken.None);

                var model = new SubscriptionDowngradeScheduledModel
                {
                    SubscriberName = subscriber?.DisplayName ?? string.Empty,
                    CurrentPlanName = change.CurrentPlanName,
                    NewPlanName = change.NewPlanName,
                    EffectiveAt = change.EffectiveAt.ToString("yyyy-MM-dd"),
                    PortalUrl = $"{frontendBaseUrl}/subscription"
                };

                await notificationSender
                    .For(change.SubscriberId)
                    .WithEmail(EmailTemplate.SubscriptionDowngradeScheduled, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, ct: CancellationToken.None);

                logger.LogInformation(
                    "Sent 7-day downgrade reminder to subscriber {SubscriberId}, effective {EffectiveAt}.",
                    change.SubscriberId, change.EffectiveAt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error sending downgrade reminder to subscriber {SubscriberId}.",
                    change.SubscriberId);
            }
        }
    }
}
