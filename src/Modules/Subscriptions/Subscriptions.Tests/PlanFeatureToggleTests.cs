using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Data;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.Plans.Features.AddPlanFeature;
using Subscriptions.Domains.Plans.Features.DeletePlanFeature;
using Subscriptions.Domains.Plans.Models;
using Xunit;

namespace Subscriptions.Tests;

public class PlanFeatureToggleTests
{
    [Fact]
    public async Task AddPlanFeature_does_not_update_plan_concurrency_state()
    {
        await using var dbContext = CreateDbContext();
        var plan = Plan.Create(
            LocalizedText.Create("Starter", "ستارتر"),
            null,
            100,
            "EGP",
            1,
            3,
            10,
            2,
            true);

        var originalUpdatedAt = plan.UpdatedAt;
        dbContext.Plans.Add(plan);
        dbContext.FeatureDefinitions.Add(PlanFeatureDefinition.Create("analytics", LocalizedText.Create("Analytics", "تحليلات")));
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var handler = new AddPlanFeatureHandler(dbContext);

        await handler.Handle(new AddPlanFeatureCommand(plan.Id, "analytics"), CancellationToken.None);

        var updatedPlan = await dbContext.Plans.AsNoTracking().SingleAsync(x => x.Id == plan.Id);
        updatedPlan.Version.Should().Be(0);
        updatedPlan.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public async Task DeletePlanFeature_does_not_update_plan_concurrency_state()
    {
        await using var dbContext = CreateDbContext();
        var plan = Plan.Create(
            LocalizedText.Create("Starter", "ستارتر"),
            null,
            100,
            "EGP",
            1,
            3,
            10,
            2,
            true);
        plan.AddFeature("analytics");
        var originalUpdatedAt = plan.UpdatedAt;

        dbContext.Plans.Add(plan);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var handler = new DeletePlanFeatureHandler(dbContext);

        await handler.Handle(new DeletePlanFeatureCommand(plan.Id, "analytics"), CancellationToken.None);

        var updatedPlan = await dbContext.Plans.AsNoTracking().SingleAsync(x => x.Id == plan.Id);
        updatedPlan.Version.Should().Be(0);
        updatedPlan.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    private static SubscriptionsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SubscriptionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SubscriptionsDbContext(options);
    }
}
