using System.Globalization;
using Microsoft.Extensions.Configuration;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>Daily job: sends renewal reminder emails 7 days before a subscription's renewal date.</summary>
[DisableConcurrentExecution(60)]
public class ProcessRenewalRemindersJob(
    IUnitOfWorkRunner unitOfWork,
    IConfiguration configuration,
    ILogger<ProcessRenewalRemindersJob> logger)
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

        var now = DateTimeOffset.UtcNow;
        var reminderFrom = now.AddDays(7);
        var reminderTo = now.AddDays(8);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";

        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Active
                        && x.RenewalDate >= reminderFrom
                        && x.RenewalDate < reminderTo)
            .Select(x => new { x.Id, x.SubscriberId, x.PlanName, x.RenewalDate })
            .ToListAsync(CancellationToken.None);

        logger.LogInformation(
            "ProcessRenewalRemindersJob: {Count} subscriptions expiring in ~7 days",
            subscriptions.Count);

        foreach (var sub in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var subscriber = await subscriberInfoProvider.GetAsync(sub.SubscriberId, CancellationToken.None);

                var model = new SubscriptionExpiringReminderModel
                {
                    SubscriberName = subscriber?.DisplayName ?? string.Empty,
                    PlanName = sub.PlanName,
                    ExpiryDate = sub.RenewalDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    RenewalUrl = $"{frontendBaseUrl}/subscription/renew"
                };

                await notificationSender
                    .For(sub.SubscriberId)
                    .WithEmail(EmailTemplate.SubscriptionExpiringReminder, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, ct: CancellationToken.None);

                logger.LogInformation(
                    "Renewal reminder sent for subscription {SubscriptionId} subscriber {SubscriberId}",
                    sub.Id,
                    sub.SubscriberId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send renewal reminder for subscription {SubscriptionId}",
                    sub.Id);
            }
        }
    }
}
