using FluentAssertions;
using Xunit;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Tests;

public class DunningStepsTests
{
    private static readonly DateTimeOffset PastDueAt = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnd = PastDueAt.AddDays(7);
    private static readonly DunningOptions Default = new();

    private static IReadOnlyList<string> Due(double daysAfterPastDue, DunningOptions? options = null) =>
        DunningProcessor.DueSteps(PastDueAt, GraceEnd, PastDueAt.AddDays(daysAfterPastDue), options ?? Default);

    [Fact]
    public void Nothing_is_due_before_the_first_step() => Due(0.5).Should().BeEmpty();

    [Theory]
    [InlineData(1, "day-1")]
    [InlineData(2.9, "day-1")]
    [InlineData(3, "day-1,day-3")]
    [InlineData(5.5, "day-1,day-3,day-5")]
    public void Steps_become_due_on_the_configured_days(double days, string expected) =>
        string.Join(',', Due(days)).Should().Be(expected);

    [Fact]
    public void The_final_notice_is_due_one_day_before_the_grace_period_ends()
    {
        Due(5.9).Should().NotContain("final");
        Due(6).Should().EndWith("final");
    }

    [Fact]
    public void Schedule_days_on_or_after_the_final_notice_are_dropped()
    {
        var shortGrace = DunningProcessor.DueSteps(PastDueAt, PastDueAt.AddDays(3), PastDueAt.AddDays(2.5), Default);

        shortGrace.Should().Equal("day-1", "final"); // day-3 and day-5 never happen: the final notice replaces them
    }

    [Fact]
    public void Custom_schedule_and_final_window_are_honoured()
    {
        var options = new DunningOptions { ScheduleDays = [2], FinalNoticeDaysBeforeGraceEnd = 2 };

        DunningProcessor.DueSteps(PastDueAt, GraceEnd, PastDueAt.AddDays(2), options).Should().Equal("day-2");
        DunningProcessor.DueSteps(PastDueAt, GraceEnd, PastDueAt.AddDays(5), options).Should().Equal("day-2", "final");
    }
}
