using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class ConcurrencyTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    [Fact]
    public async Task Concurrent_create_for_the_same_subscriber_lets_exactly_one_win_and_the_other_gets_409()
    {
        var plan = await fx.CreatePlanAsync("Race", 100m);
        var subscriberId = Guid.NewGuid();

        var attempts = Enumerable.Range(0, 2)
            .Select(_ => Capture(() => fx.SendAs(subscriberId, new CreateSubscriptionCommand(subscriberId, plan.Id))))
            .ToList();
        var results = await Task.WhenAll(attempts);

        results.Count(x => x.Error is null).Should().Be(1);
        results.Single(x => x.Error is not null).Error.Should().BeAssignableTo<ConflictException>(
            "the loser hits either the pre-check or the one-open-subscription unique index (23505 → 409)");

        var open = await fx.WithDb(db => db.Subscriptions.CountAsync(x =>
            x.SubscriberId == subscriberId &&
            (x.Status == SubscriptionStatus.PendingApproval || x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue)));
        open.Should().Be(1);
    }

    [Fact]
    public async Task Database_rejects_a_second_open_subscription_even_without_the_handler_pre_check()
    {
        var plan = await fx.CreatePlanAsync("Index", 100m);
        var subscriberId = Guid.NewGuid();
        await _scenario.CreateAsync(subscriberId, plan);

        var act = () => fx.WithDb(async db =>
        {
            var snapshot = PlanSnapshot.From(plan, plan.Price);
            db.Subscriptions.Add(Subscription.Create(subscriberId, snapshot, plan.Price, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        });

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("*already has an active or pending subscription*");
    }

    [Fact]
    public async Task Concurrent_approvals_of_the_same_payment_let_one_win_and_issue_one_invoice()
    {
        var plan = await fx.CreatePlanAsync("DoubleApprove", 230m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Capture(() =>
            fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId)))));

        results.Count(x => x.Error is null).Should().Be(1);
        results.Single(x => x.Error is not null).Error
            .Should().Match<Exception>(e => e is ConcurrencyConflictException || e is ConflictException, "both map to 409");

        var invoices = await _scenario.InvoicesAsync(created.SubscriptionId);
        invoices.Should().ContainSingle(x => x.PaymentId == created.PaymentId);
        (await _scenario.LoadAsync(created.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Twenty_parallel_approvals_get_gapless_unique_invoice_numbers()
    {
        var plan = await fx.CreatePlanAsync("Gapless", 115m);
        var pending = new List<CreateSubscriptionResult>();
        for (var i = 0; i < 20; i++)
        {
            var subscriberId = Guid.NewGuid();
            var created = await _scenario.CreateAsync(subscriberId, plan);
            await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
            pending.Add(created);
        }

        var approvals = await Task.WhenAll(pending.Select(p =>
            fx.SendAsAdmin(new ApproveSubscriptionCommand(p.SubscriptionId, TestIds.Admin, p.PaymentId))));

        approvals.Select(x => x.InvoiceNumber).Should().OnlyHaveUniqueItems();
        approvals.Should().AllSatisfy(x => x.InvoiceNumber.Should().MatchRegex(@"^INV-\d{4}-\d{6}$"));

        // Global check over every document issued in this database (all tests, incl. failed/rolled-back races).
        var numbers = await fx.WithDb(db => db.Invoices.Select(x => x.InvoiceNumber).ToListAsync());
        foreach (var yearGroup in numbers.GroupBy(n => n.Split('-')[1]))
        {
            var sequence = yearGroup.Select(n => long.Parse(n.Split('-')[2])).OrderBy(x => x).ToList();
            sequence.Should().OnlyHaveUniqueItems();
            sequence.Should().Equal(Enumerable.Range(1, sequence.Count).Select(x => (long)x), "numbers must have no gaps");
        }
    }

    private static async Task<(T? Result, Exception? Error)> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception ex)
        {
            return (default, ex);
        }
    }
}
