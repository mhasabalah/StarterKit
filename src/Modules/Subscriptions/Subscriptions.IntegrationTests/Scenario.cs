using Microsoft.EntityFrameworkCore;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

/// <summary>Reusable flows driven through the real MediatR pipeline.</summary>
public sealed class Scenario(SubscriptionsPostgresFixture fx)
{
    public async Task<CreateSubscriptionResult> CreateAsync(Guid subscriberId, Plan plan) =>
        await fx.SendAs(subscriberId, new CreateSubscriptionCommand(subscriberId, plan.Id));

    /// <param name="content">Proof bytes; default unique per call (so proofs are not duplicates of each other).</param>
    public async Task<UploadPaymentProofResult> UploadActivationProofAsync(
        Guid subscriberId, Guid subscriptionId, Guid paymentId, byte[]? content = null) =>
        await fx.SendAs(subscriberId, new UploadPaymentProofCommand(
            subscriptionId, paymentId, subscriberId, Png(content), "proof.png", "image/png", 128, fx.PaymentMethodId));

    public async Task<UploadPaymentProofResult> UploadRenewalProofAsync(Guid subscriberId, Guid subscriptionId, Guid? paymentId = null) =>
        await fx.SendAs(subscriberId, new UploadRenewalProofCommand(
            subscriptionId, paymentId, subscriberId, Png(), "proof.png", "image/png", 128, fx.PaymentMethodId));

    public async Task<UploadPaymentProofResult> UploadProrationProofAsync(Guid subscriberId, Guid subscriptionId, Guid paymentId) =>
        await fx.SendAs(subscriberId, new UploadProrationProofCommand(
            subscriptionId, paymentId, subscriberId, Png(), "proof.png", "image/png", 128, fx.PaymentMethodId));

    /// <summary>Creates, submits the activation proof and approves: an Active subscription with its activation invoice.</summary>
    public async Task<(Guid SubscriberId, Guid SubscriptionId, ApproveSubscriptionResult Approval)> ActiveAsync(Plan plan)
    {
        var subscriberId = Guid.NewGuid();
        var created = await CreateAsync(subscriberId, plan);
        await UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        var approval = await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId));
        return (subscriberId, created.SubscriptionId, approval);
    }

    public Task<Subscription> LoadAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.Subscriptions
            .AsNoTracking()
            .Include(x => x.ScheduledPlanChanges)
            .SingleAsync(x => x.Id == subscriptionId));

    public Task<List<SubscriptionPayment>> PaymentsAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.Payments.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.CreatedAt).ToListAsync());

    public Task<List<SubscriptionInvoice>> InvoicesAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.Invoices.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.IssuedAt).ToListAsync());

    /// <summary>PNG magic + a random tail, so two default proofs never share a content hash.</summary>
    private static MemoryStream Png(byte[]? content = null) =>
        new(content ?? [0x89, 0x50, 0x4E, 0x47, .. Guid.NewGuid().ToByteArray()]);
}
