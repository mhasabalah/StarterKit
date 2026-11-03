using Microsoft.Extensions.Configuration;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Plans.Features.CancelScheduledPriceChange;

public class CancelScheduledPriceChangeCommandValidator : AbstractValidator<CancelScheduledPriceChangeCommand>
{
    public CancelScheduledPriceChangeCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

public class CancelScheduledPriceChangeHandler(
    IPlanRepository planRepository,
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit,
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration)
    : ICommandHandler<CancelScheduledPriceChangeCommand, Unit>
{
    public async Task<Unit> Handle(CancelScheduledPriceChangeCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        if (!plan.IsActive || plan.IsDeleted)
            throw new PlanNotFoundException(command.PlanId);

        // Capture before clearing so we can include it in the notification.
        var cancelledPendingPrice = plan.PendingPrice;
        var cancelledEffectiveAt = plan.PriceEffectiveAt;

        plan.CancelPriceChange();

        audit.Record(
            "CancelScheduledPriceChange",
            plan.Id,
            "Plan",
            reason: $"Cancelled scheduled price change to {plan.Currency} {cancelledPendingPrice:F2} that was effective {cancelledEffectiveAt:yyyy-MM-dd}",
            amount: cancelledPendingPrice, currency: plan.Currency, before: new { PendingPrice = cancelledPendingPrice, PriceEffectiveAt = cancelledEffectiveAt }, after: new { PendingPrice = (decimal?)null },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);

        await NotifySubscribersAsync(plan, cancelledPendingPrice, cancelledEffectiveAt, cancellationToken);

        return Unit.Value;
    }

    private async Task NotifySubscribersAsync(
        Plan plan,
        decimal? cancelledPendingPrice,
        DateTimeOffset? cancelledEffectiveAt,
        CancellationToken cancellationToken)
    {
        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";

        var subscriberIds = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(s => s.PlanId == plan.Id &&
                        (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue))
            .Select(s => s.SubscriberId)
            .Distinct()
            .ToListAsync(CancellationToken.None);

        foreach (var subscriberId in subscriberIds)
        {
            try
            {
                var subscriber = await subscriberInfoProvider.GetAsync(subscriberId, CancellationToken.None);

                var model = new PriceChangeCancelledNotificationModel
                {
                    SubscriberName = subscriber?.DisplayName ?? string.Empty,
                    PlanName = plan.Name.En,
                    CurrentPrice = plan.Price.ToString("F2"),
                    Currency = plan.Currency,
                    PortalUrl = $"{frontendBaseUrl}/subscription"
                };

                await notificationSender
                    .For(subscriberId)
                    .WithEmail(EmailTemplate.PriceChangeCancelled, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, ct: CancellationToken.None);
            }
            catch
            {
                // Notification failure must not roll back the cancellation.
            }
        }
    }
}
