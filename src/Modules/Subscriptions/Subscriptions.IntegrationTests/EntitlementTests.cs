using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Entitlements;
using Subscriptions.Contracts.Subscribers;
using Subscriptions.Data;
using Subscriptions.Domains.Subscriptions.Entitlements;
using Subscriptions.Options;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public class EntitlementTests(SubscriptionsPostgresFixture fx)
{
    private const string Feature = "advanced-reports";

    /// <summary>The real service wired with Enforce=true over the module's real DbContext and cache.</summary>
    private async Task<T> EnforcedAsync<T>(Guid subscriberId, Func<IEntitlementService, Task<T>> act)
    {
        fx.Http.SetUser(subscriberId, admin: false);
        T result = default!;
        await fx.WithScope(async sp =>
        {
            var service = new PlanEntitlementService(
                sp.GetRequiredService<SubscriptionsDbContext>(),
                sp.GetRequiredService<ISubscriberIdProvider>(),
                sp.GetRequiredService<IEntitlementCache>(),
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = true } }),
                NullLogger<PlanEntitlementService>.Instance);
            result = await act(service);
        });
        return result;
    }

    [Fact]
    public async Task Enforced_ActivePlanFeatures_AreAllowed_AndOthersDenied_WithPlanLimits()
    {
        var plan = await fx.CreatePlanAsync("Pro-" + Guid.NewGuid(), 100m, "SAR", 1, Feature);
        var (subscriberId, _, _) = await new Scenario(fx).ActiveAsync(plan);

        (await EnforcedAsync(subscriberId, s => s.HasFeatureAsync(Feature))).Should().BeTrue();
        (await EnforcedAsync(subscriberId, s => s.HasFeatureAsync("not-in-plan"))).Should().BeFalse();
        (await EnforcedAsync(subscriberId, s => s.GetLimitAsync(EntitlementKeys.MaxUsers))).Should().Be(3);
        (await EnforcedAsync(subscriberId, s => s.GetLimitAsync(EntitlementKeys.MaxItems))).Should().Be(10);
    }

    [Fact]
    public async Task Enforced_SubscriberWithoutSubscription_HasNothing()
    {
        (await EnforcedAsync(Guid.NewGuid(), s => s.HasFeatureAsync(Feature))).Should().BeFalse();
    }

    [Fact]
    public async Task Default_NotEnforced_AllowsAnyFeature_AndNoLimit()
    {
        var stranger = Guid.NewGuid();
        fx.Http.SetUser(stranger, admin: false);
        bool has = false;
        int? limit = 1;
        await fx.WithScope(async sp =>
        {
            var service = sp.GetRequiredService<IEntitlementService>();
            has = await service.HasFeatureAsync(Feature);
            limit = await service.GetLimitAsync(EntitlementKeys.MaxUsers);
        });

        has.Should().BeTrue();
        limit.Should().BeNull();
    }

    [Fact]
    public async Task Activation_InvalidatesCachedNoSubscriptionAnswer()
    {
        var plan = await fx.CreatePlanAsync("Pro-" + Guid.NewGuid(), 100m, "SAR", 1, Feature);
        var scenario = new Scenario(fx);
        var subscriberId = Guid.NewGuid();

        // Cached as "nothing" (no active subscription yet).
        (await EnforcedAsync(subscriberId, s => s.HasFeatureAsync(Feature))).Should().BeFalse();

        var created = await scenario.CreateAsync(subscriberId, plan);
        await scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await fx.SendAsAdmin(new Subscriptions.Contracts.Subscriptions.Commands.ApproveSubscriptionCommand(
            created.SubscriptionId, TestIds.Admin, created.PaymentId));

        // The activation domain event dropped the cached answer; well within the 60 s TTL.
        (await EnforcedAsync(subscriberId, s => s.HasFeatureAsync(Feature))).Should().BeTrue();
    }
}
