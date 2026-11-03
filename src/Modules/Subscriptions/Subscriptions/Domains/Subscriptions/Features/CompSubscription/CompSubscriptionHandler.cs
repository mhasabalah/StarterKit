using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.CompSubscription;

public class CompSubscriptionCommandValidator : AbstractValidator<CompSubscriptionCommand>
{
    public CompSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Months).InclusiveBetween(1, Subscription.MaxComplimentaryMonths);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Complimentary activation on a chosen plan (<see cref="IComplimentaryActivationService"/>): 0-total invoice,
/// ledger Adjustment 0, owed payments waived, audited. One transaction.
/// </summary>
public class CompSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    IComplimentaryActivationService complimentary,
    IPaymentSettlementService settlement,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CompSubscriptionCommand, CompSubscriptionResult>
{
    public async Task<CompSubscriptionResult> Handle(CompSubscriptionCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || plan.IsDeleted || !plan.IsActive)
            throw new PlanNotFoundException(command.PlanId);

        var now = timeProvider.GetUtcNow();
        var before = new { Status = subscription.Status.ToString(), subscription.PlanId, subscription.PlanName };

        var comp = await complimentary.ActivateAsync(
            subscription,
            PlanSnapshot.From(plan, PricingPolicy.PlanPriceAt(plan, now)),
            command.Months,
            command.AdminId,
            command.Reason,
            now,
            cancellationToken);

        audit.Record(
            "CompSubscription",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            command.Reason,
            0m,
            subscription.Currency,
            before: before,
            after: new
            {
                Status = subscription.Status.ToString(),
                subscription.PlanId,
                subscription.PlanName,
                command.Months,
                subscription.RenewalDate,
                PaymentId = comp.Payment.Id,
                InvoiceId = comp.Invoice.Id,
                comp.Invoice.InvoiceNumber,
                comp.WaivedPaymentIds
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await settlement.AfterCommitAsync(
            subscription, new SettlementResult(comp.Invoice, true, comp.FromStatus, subscription.Status), cancellationToken);

        return new CompSubscriptionResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.PlanId,
            subscription.PlanName,
            subscription.StartDate!.Value,
            subscription.RenewalDate!.Value,
            comp.Payment.Id,
            comp.Invoice.Id,
            comp.Invoice.InvoiceNumber);
    }
}
