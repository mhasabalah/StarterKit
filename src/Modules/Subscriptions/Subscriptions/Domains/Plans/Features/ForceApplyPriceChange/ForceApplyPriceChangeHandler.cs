using Microsoft.Extensions.Configuration;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Plans.Features.ForceApplyPriceChange;

public class ForceApplyPriceChangeCommandValidator : AbstractValidator<ForceApplyPriceChangeCommand>
{
    public ForceApplyPriceChangeCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.NewPrice).GreaterThan(0);
    }
}

public class ForceApplyPriceChangeHandler(
    IPlanRepository planRepository,
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit,
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration)
    : ICommandHandler<ForceApplyPriceChangeCommand, Unit>
{
    public async Task<Unit> Handle(ForceApplyPriceChangeCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        if (!plan.IsActive || plan.IsDeleted)
            throw new PlanNotFoundException(command.PlanId);

        if (command.NewPrice == plan.Price)
            throw new ConflictException("New price is the same as the current price.");

        var oldPrice = plan.Price;
        var hadPendingSchedule = plan.PendingPrice is not null;

        plan.ForcePrice(command.NewPrice);

        audit.Record(
            "ForceApplyPriceChange",
            plan.Id,
            "Plan",
            reason: $"Forced immediate price change from {plan.Currency} {oldPrice:F2} to {plan.Currency} {command.NewPrice:F2}"
                + (hadPendingSchedule ? " (cancelled existing scheduled change)" : string.Empty),
            amount: command.NewPrice, currency: plan.Currency, before: new { Price = oldPrice, HadPendingSchedule = hadPendingSchedule }, after: new { Price = command.NewPrice },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);

        await NotifySubscribersAsync(plan, oldPrice, cancellationToken);

        return Unit.Value;
    }

    private async Task NotifySubscribersAsync(
        Plan plan,
        decimal oldPrice,
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

                var model = new PriceChangeNotificationModel
                {
                    SubscriberName = subscriber?.DisplayName ?? string.Empty,
                    PlanName = plan.Name.En,
                    OldPrice = oldPrice.ToString("F2"),
                    NewPrice = plan.Price.ToString("F2"),
                    Currency = plan.Currency,
                    EffectiveAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                    DaysUntilEffective = "0",
                    PortalUrl = $"{frontendBaseUrl}/subscription"
                };

                await notificationSender
                    .For(subscriberId)
                    .WithEmail(EmailTemplate.PriceChangeNotification, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, ct: CancellationToken.None);
            }
            catch
            {
                // Notification failure must not roll back the price change.
            }
        }
    }
}
