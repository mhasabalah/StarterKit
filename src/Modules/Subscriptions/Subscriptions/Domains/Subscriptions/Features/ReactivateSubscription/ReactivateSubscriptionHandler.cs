using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Features.ReactivateSubscription;

public class ReactivateSubscriptionCommandValidator : AbstractValidator<ReactivateSubscriptionCommand>
{
    public ReactivateSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Months)
            .NotNull().InclusiveBetween(1, Subscription.MaxComplimentaryMonths)
            .When(x => x.Complimentary);
        RuleFor(x => x.Months).Null().When(x => !x.Complimentary)
            .WithMessage("Months only applies to a complimentary reactivation.");
    }
}

/// <summary>
/// Reopens a recently Cancelled/Expired subscription: PendingApproval with a new activation payment at the current
/// plan price (renewal pricing rules), or Active at zero price (<see cref="IComplimentaryActivationService"/>).
/// The one-open-subscription index turns "subscriber already has another open subscription" into a 409.
/// </summary>
public class ReactivateSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    IComplimentaryActivationService complimentary,
    IPaymentSettlementService settlement,
    ISubscriberInfoProvider subscriberInfoProvider,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<ReactivateSubscriptionCommand, ReactivateSubscriptionResult>
{
    public async Task<ReactivateSubscriptionResult> Handle(
        ReactivateSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var now = timeProvider.GetUtcNow();
        var before = new
        {
            Status = subscription.Status.ToString(),
            subscription.EndDate,
            subscription.CancelledAt,
            subscription.PlanId,
            subscription.Price
        };
        var plan = await planRepository.GetByIdAsync(subscription.PlanId, cancellationToken);

        SubscriptionPayment payment;
        SubscriptionInvoice? invoice = null;
        if (command.Complimentary)
        {
            var snapshot = plan is { IsDeleted: false }
                ? PlanSnapshot.From(plan, PricingPolicy.PlanPriceAt(plan, now))
                : subscription.CurrentPlanSnapshot();
            var comp = await complimentary.ActivateAsync(
                subscription, snapshot, command.Months!.Value, command.AdminId, command.Reason, now, cancellationToken);
            payment = comp.Payment;
            invoice = comp.Invoice;
        }
        else
        {
            var from = subscription.Status;
            var periodPrice = PricingPolicy.RenewalPeriodPrice(subscription, plan, now);
            var amountDue = TaxCalculator.AmountDue(periodPrice, options.Value.Tax.ToPolicy(), subscription.Currency);
            payment = subscription.Reactivate(periodPrice, amountDue, options.Value.ReactivationWindowDays, now);

            dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                from,
                subscription.Status,
                command.AdminId,
                "Admin",
                $"Reactivated: {command.Reason}"));
            await PaymentSettlementService.RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, cancellationToken);
        }

        audit.Record(
            command.Complimentary ? "ReactivateSubscriptionComplimentary" : "ReactivateSubscription",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            command.Reason,
            payment.Amount,
            payment.Currency,
            before: before,
            after: new
            {
                Status = subscription.Status.ToString(),
                PaymentId = payment.Id,
                AmountDue = payment.Amount,
                subscription.RenewalDate,
                InvoiceId = invoice?.Id,
                invoice?.InvoiceNumber
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await settlement.AfterCommitAsync(
            subscription,
            invoice is null ? null : new SettlementResult(invoice, true, subscription.Status, subscription.Status),
            cancellationToken);

        return new ReactivateSubscriptionResult(
            subscription.Id,
            subscription.Status.ToString(),
            payment.Id,
            payment.Amount,
            payment.Currency,
            subscription.RenewalDate,
            invoice?.Id,
            invoice?.InvoiceNumber);
    }
}
