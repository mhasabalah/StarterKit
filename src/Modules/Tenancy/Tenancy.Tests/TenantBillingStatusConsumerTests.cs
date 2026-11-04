using MassTransit;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Subscriptions.Contracts.Subscriptions.Queries;
using Tenancy.Data;
using Tenancy.Domains.Tenants.Models;
using Tenancy.Services;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tenancy.Tests;

/// <summary>
/// Billing consumer: the integration event is only a trigger, the decision comes from the CURRENT status of the
/// subscriber (so duplicates and out-of-order deliveries are harmless).
/// </summary>
public sealed class TenantBillingStatusConsumerTests
{
    private const string BillingReason = TenantBillingStatusConsumer.BillingSuspensionReason;

    private readonly string _database = $"billing-{Guid.NewGuid():N}";
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();

    private TenancyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseInMemoryDatabase(_database).Options);

    private async Task<Tenant> SeedTenantAsync(Action<Tenant>? configure = null)
    {
        var tenant = Tenant.Create($"t{Guid.NewGuid():N}"[..12], "Acme", Guid.NewGuid(), TenantIsolation.Shared);
        configure?.Invoke(tenant);
        await using var db = NewContext();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<Tenant> ReloadAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private void SubscriptionStatusIs(SubscriptionStatus status) =>
        _sender.Setup(s => s.Send(It.IsAny<GetSubscriberSubscriptionStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetSubscriberSubscriptionStatusQuery q, CancellationToken _) =>
                new GetSubscriberSubscriptionStatusResult(q.SubscriberId, status, null, null, 0, 0, 0, []));

    private TenantBillingStatusConsumer NewConsumer(TenancyDbContext db, bool multiTenancy) =>
        new(db, _sender.Object, _dispatcher.Object,
            MsOptions.Create(new MultiTenancyOptions { Enabled = multiTenancy }),
            NullLogger<TenantBillingStatusConsumer>.Instance);

    private async Task ConsumeExpiredAsync(Guid subscriberId, bool multiTenancy = true)
    {
        var context = new Mock<ConsumeContext<SubscriptionExpiredIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new SubscriptionExpiredIntegrationEvent
        { SubscriberId = subscriberId, SubscriptionId = Guid.NewGuid() });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await using var db = NewContext();
        await NewConsumer(db, multiTenancy).Consume(context.Object);
    }

    private async Task ConsumeActivatedAsync(Guid subscriberId, bool multiTenancy = true)
    {
        var context = new Mock<ConsumeContext<SubscriptionActivatedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new SubscriptionActivatedIntegrationEvent
        { SubscriberId = subscriberId, SubscriptionId = Guid.NewGuid() });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await using var db = NewContext();
        await NewConsumer(db, multiTenancy).Consume(context.Object);
    }

    private void DispatchedTimes(Times times) =>
        _dispatcher.Verify(d => d.DispatchPendingAsync(It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Expired_subscription_suspends_an_active_tenant_with_the_billing_reason()
    {
        var tenant = await SeedTenantAsync();
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeExpiredAsync(tenant.Id);

        var stored = await ReloadAsync(tenant.Id);
        stored.Status.Should().Be(TenantStatus.Suspended);
        stored.StatusReason.Should().Be(BillingReason);
        DispatchedTimes(Times.Once());
    }

    [Fact]
    public async Task Activated_subscription_reactivates_a_billing_suspended_tenant()
    {
        var tenant = await SeedTenantAsync(t => t.Suspend(BillingReason));
        SubscriptionStatusIs(SubscriptionStatus.Active);

        await ConsumeActivatedAsync(tenant.Id);

        var stored = await ReloadAsync(tenant.Id);
        stored.Status.Should().Be(TenantStatus.Active);
        stored.StatusReason.Should().BeNull();
        DispatchedTimes(Times.Once());
    }

    [Fact]
    public async Task Stale_expired_event_does_not_suspend_a_tenant_whose_subscription_is_active_again()
    {
        var tenant = await SeedTenantAsync();
        SubscriptionStatusIs(SubscriptionStatus.Active); // out of order: Expired arrives after the renewal

        await ConsumeExpiredAsync(tenant.Id);

        (await ReloadAsync(tenant.Id)).Status.Should().Be(TenantStatus.Active);
        DispatchedTimes(Times.Never());
    }

    [Fact]
    public async Task Stale_activated_event_does_not_lift_a_suspension_while_the_subscription_is_expired()
    {
        var tenant = await SeedTenantAsync(t => t.Suspend(BillingReason));
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeActivatedAsync(tenant.Id);

        (await ReloadAsync(tenant.Id)).Status.Should().Be(TenantStatus.Suspended);
        DispatchedTimes(Times.Never());
    }

    [Fact]
    public async Task An_operator_suspension_with_another_reason_is_never_lifted()
    {
        var tenant = await SeedTenantAsync(t => t.Suspend("Abuse report 42"));
        SubscriptionStatusIs(SubscriptionStatus.Active);

        await ConsumeActivatedAsync(tenant.Id);

        var stored = await ReloadAsync(tenant.Id);
        stored.Status.Should().Be(TenantStatus.Suspended);
        stored.StatusReason.Should().Be("Abuse report 42");
        DispatchedTimes(Times.Never());
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.PendingApproval)]
    public async Task Other_subscription_states_change_nothing(SubscriptionStatus status)
    {
        var tenant = await SeedTenantAsync();
        SubscriptionStatusIs(status);

        await ConsumeExpiredAsync(tenant.Id);

        (await ReloadAsync(tenant.Id)).Status.Should().Be(TenantStatus.Active);
        DispatchedTimes(Times.Never());
    }

    [Fact]
    public async Task Duplicate_delivery_is_idempotent()
    {
        var tenant = await SeedTenantAsync();
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeExpiredAsync(tenant.Id);
        var afterFirst = await ReloadAsync(tenant.Id);
        await ConsumeExpiredAsync(tenant.Id);
        var afterSecond = await ReloadAsync(tenant.Id);

        afterSecond.Status.Should().Be(TenantStatus.Suspended);
        afterSecond.StatusReason.Should().Be(BillingReason);
        DispatchedTimes(Times.Once());
    }

    [Fact]
    public async Task Disabled_multi_tenancy_is_a_no_op()
    {
        var tenant = await SeedTenantAsync();
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeExpiredAsync(tenant.Id, multiTenancy: false);

        (await ReloadAsync(tenant.Id)).Status.Should().Be(TenantStatus.Active);
        _sender.Verify(s => s.Send(It.IsAny<GetSubscriberSubscriptionStatusQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        DispatchedTimes(Times.Never());
    }

    [Fact]
    public async Task The_default_tenant_is_never_touched()
    {
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeExpiredAsync(TenantDefaults.DefaultTenantId);

        _sender.Verify(s => s.Send(It.IsAny<GetSubscriberSubscriptionStatusQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        DispatchedTimes(Times.Never());
    }

    [Fact]
    public async Task A_subscriber_that_is_not_a_tenant_is_ignored()
    {
        SubscriptionStatusIs(SubscriptionStatus.Expired);

        await ConsumeExpiredAsync(Guid.NewGuid());

        _sender.Verify(s => s.Send(It.IsAny<GetSubscriberSubscriptionStatusQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        DispatchedTimes(Times.Never());
    }
}
