using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Shared.Core.Exceptions;
using Subscriptions.Data;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// Regression guard for the "domain events raised in Hangfire jobs are never published" bug: the jobs and the renewal
/// orchestrator run outside the mediator, so before <c>IUnitOfWorkRunner</c> their events were collected and dropped
/// (e.g. no "subscription expired" email ever left ProcessExpiredSubscriptionsJob).
/// </summary>
[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class JobDomainEventTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    private IRenewalOrchestrator Orchestrator => fx.Services.GetRequiredService<IRenewalOrchestrator>();

    [Fact]
    public async Task Expiry_job_publishes_SubscriptionExpiredEvent_and_its_handler_sends_the_email()
    {
        var plan = await fx.CreatePlanAsync("JobEvents-Expire", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));

        // Grace period already over at the job's "now" (the job reads the real clock).
        await fx.WithDb(db => db.Subscriptions
            .Where(x => x.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.GracePeriodEndsAt, DateTimeOffset.UtcNow.AddDays(-1))));

        await fx.Services.GetRequiredService<ProcessExpiredSubscriptionsJob>()
            .ExecuteAsync(new JobCancellationToken(false));

        (await _scenario.LoadAsync(subscriptionId)).Status.Should().Be(SubscriptionStatus.Expired);
        EmailsTo(subscriberId, EmailTemplate.SubscriptionExpired).Should().Be(1,
            "SubscriptionExpiredEventHandler must run exactly once from the job path");
    }

    [Fact]
    public async Task Renewal_boundary_publishes_SubscriptionPastDueEvent_once()
    {
        var plan = await fx.CreatePlanAsync("JobEvents-Boundary", 100m);
        var (billedSubscriber, billed, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(billed)).RenewalDate!.Value;

        var outcome = await Orchestrator.ProcessBoundaryAsync(billed, renewal.AddHours(1));

        outcome.Should().Be(RenewalBoundaryOutcome.Billed);
        EmailsTo(billedSubscriber, EmailTemplate.SubscriptionPastDue).Should().Be(1,
            "SubscriptionPastDueEventHandler must run from the orchestrator (job) path");

        // Re-running the boundary is a no-op and publishes nothing again.
        var again = await Orchestrator.ProcessBoundaryAsync(billed, DateTimeOffset.UtcNow.AddYears(1));
        again.Should().Be(RenewalBoundaryOutcome.Skipped);
        EmailsTo(billedSubscriber, EmailTemplate.SubscriptionPastDue).Should().Be(1);
    }

    [Fact]
    public async Task Subscriptions_DbContext_lets_concurrency_exceptions_through_untranslated()
    {
        // SubscriptionsDbContext maps 23505 → ConflictException; it must not swallow or rewrap concurrency conflicts
        // (jobs catch DbUpdateConcurrencyException per item; the mediator pipeline translates it to 409).
        var plan = await fx.CreatePlanAsync("JobEvents-Race", 100m);

        await using var winnerScope = fx.Services.CreateAsyncScope();
        await using var loserScope = fx.Services.CreateAsyncScope();
        var winnerDb = winnerScope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var winner = await winnerDb.Plans.SingleAsync(x => x.Id == plan.Id);
        var loser = await loserDb.Plans.SingleAsync(x => x.Id == plan.Id);

        winner.SchedulePriceChange(150m, DateTimeOffset.UtcNow.AddDays(60));
        await winnerDb.SaveChangesAsync();
        loser.SchedulePriceChange(160m, DateTimeOffset.UtcNow.AddDays(60));

        var save = () => loserDb.SaveChangesAsync();

        var thrown = await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        thrown.Which.IsConcurrencyConflict().Should().BeTrue();
    }

    private int EmailsTo(Guid subscriberId, EmailTemplate template) =>
        fx.Notifications.Messages.Count(m => m.UserId == subscriberId && m.Email?.Template == template);
}
