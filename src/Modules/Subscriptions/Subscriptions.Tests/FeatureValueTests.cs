using FluentAssertions;
using Xunit;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Plans.Models;

namespace Subscriptions.Tests;

public class FeatureValueTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1000/Monthly", true)]
    [InlineData("5/Never", true)]
    [InlineData("nonsense", false)]
    [InlineData(null, false)]
    public void Metered_values_count_as_enabled(string? value, bool enabled) =>
        FeatureValue.IsEnabled(value).Should().Be(enabled);

    [Fact]
    public void A_quota_value_round_trips()
    {
        FeatureValue.TryParseQuota(FeatureValue.Format(1000, QuotaResetPeriod.Never), out var limit, out var period)
            .Should().BeTrue();
        limit.Should().Be(1000);
        period.Should().Be(QuotaResetPeriod.Never);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("-5/Monthly")]
    [InlineData("10/Weekly")]
    [InlineData("10/")]
    public void Plain_and_malformed_values_are_not_quotas(string value) =>
        FeatureValue.TryParseQuota(value, out _, out _).Should().BeFalse();

    [Fact]
    public void Plan_snapshot_carries_the_limit_of_a_metered_feature()
    {
        var plan = Plan.Create(Shared.Contracts.Localization.LocalizedText.Create("P", "P"), null, 10m, "SAR", 1, null, null, null, true);
        plan.AddFeature("reports");
        plan.AddFeature("api_calls", 1000, QuotaResetPeriod.Monthly);

        var features = FeaturesJson.Deserialize(FeaturesJson.FromPlan(plan));

        features["reports"].Should().Be("true");
        features["api_calls"].Should().Be("1000/Monthly");
    }

    [Fact]
    public void A_negative_limit_is_rejected() =>
        FluentActions.Invoking(() => PlanFeature.Create(Guid.NewGuid(), "x", -1)).Should()
            .Throw<Shared.Core.Exceptions.BadRequestException>();

    [Fact]
    public void Trial_days_are_bounded()
    {
        FluentActions.Invoking(() => Plan.Create(Shared.Contracts.Localization.LocalizedText.Create("P", "P"), null, 10m, "SAR", 1, null, null, null, true, trialDays: 400))
            .Should().Throw<Shared.Core.Exceptions.BadRequestException>();
        Plan.Create(Shared.Contracts.Localization.LocalizedText.Create("P", "P"), null, 10m, "SAR", 1, null, null, null, true, trialDays: 14)
            .TrialDays.Should().Be(14);
    }
}
