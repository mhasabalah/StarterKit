using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Subscriptions.Contracts.Ledger.DTOs;
using Subscriptions.Data;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.Ledger.Events;
using Subscriptions.Domains.Ledger.Models;
using Subscriptions.Domains.Ledger.Services;
using Subscriptions.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Subscriptions.Tests.Billing;

/// <summary>
/// The billing dashboard cache: one invalidation drops every cached range, a broken cache never fails the request,
/// and every ledger write stages the event that triggers the invalidation.
/// </summary>
public class BillingDashboardCacheTests
{
    private static readonly DateTimeOffset From = new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BillingDashboardCache NewCache(IDistributedCache store, int minutes = 5) =>
        new(store,
            MsOptions.Create(new SubscriptionOptions { BillingDashboardCacheMinutes = minutes }),
            NullLogger<BillingDashboardCache>.Instance);

    private static MemoryDistributedCache NewStore() =>
        new(MsOptions.Create(new MemoryDistributedCacheOptions()));

    private static BillingDashboardDto Dashboard(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, from, [], [], [], [], [], 0, null, 0, 0, 0, 0, 0);

    [Fact]
    public async Task Invalidate_DropsEveryCachedRange()
    {
        var cache = NewCache(NewStore());
        await cache.SetAsync(From, From.AddMonths(1), Dashboard(From, From.AddMonths(1)));
        await cache.SetAsync(From, From.AddYears(1), Dashboard(From, From.AddYears(1)));
        (await cache.GetAsync(From, From.AddMonths(1))).Should().NotBeNull();

        await cache.InvalidateAsync();

        (await cache.GetAsync(From, From.AddMonths(1))).Should().BeNull();
        (await cache.GetAsync(From, From.AddYears(1))).Should().BeNull();
    }

    [Fact]
    public async Task Set_AfterInvalidate_IsServedAgain()
    {
        var cache = NewCache(NewStore());
        await cache.InvalidateAsync();

        await cache.SetAsync(From, From.AddDays(1), Dashboard(From, From.AddDays(1)));

        (await cache.GetAsync(From, From.AddDays(1)))!.To.Should().Be(From.AddDays(1));
    }

    [Fact]
    public async Task ZeroMinutes_DisablesCaching()
    {
        var cache = NewCache(NewStore(), minutes: 0);

        await cache.SetAsync(From, From.AddDays(1), Dashboard(From, From.AddDays(1)));

        (await cache.GetAsync(From, From.AddDays(1))).Should().BeNull();
    }

    [Fact]
    public async Task BrokenStore_NeverThrows()
    {
        var store = new Mock<IDistributedCache>();
        store.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        store.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var cache = NewCache(store.Object);

        (await cache.GetAsync(From, From.AddDays(1))).Should().BeNull();
        await cache.Invoking(c => c.SetAsync(From, From.AddDays(1), Dashboard(From, From.AddDays(1)))).Should().NotThrowAsync();
        await cache.Invoking(c => c.InvalidateAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public void LedgerAdd_StagesLedgerEntryRecordedEvent_OnTheSameContext()
    {
        using var db = new SubscriptionsDbContext(
            new DbContextOptionsBuilder<SubscriptionsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var collector = new Mock<IDomainEventCollector>();
        var subscription = SubscriptionFactory.Active();
        var entry = LedgerEntry.ForProviderRefund(
            subscription, Guid.NewGuid(), 10m, "SAR", null, Guid.NewGuid(), From, "Stripe", "re_1");

        new LedgerRepository(db, collector.Object).Add(entry);

        collector.Verify(x => x.Stage(db, It.Is<IReadOnlyCollection<INotification>>(events =>
            events.OfType<LedgerEntryRecordedEvent>().Single().LedgerEntryId == entry.Id)), Times.Once);
        db.ChangeTracker.Entries<LedgerEntry>().Should().ContainSingle(e => e.Entity == entry);
    }
}
