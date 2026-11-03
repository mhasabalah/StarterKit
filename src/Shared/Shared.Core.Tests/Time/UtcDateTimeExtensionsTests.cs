using FluentAssertions;
using Shared.Core.Extensions;
using Xunit;

namespace Shared.Core.Tests.Time;

public sealed class UtcDateTimeExtensionsTests
{
    [Fact]
    public void Unspecified_is_taken_as_utc_without_shifting_the_clock_time()
    {
        var value = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Unspecified);

        var utc = value.AsUtc();

        utc.Kind.Should().Be(DateTimeKind.Utc);
        utc.Should().Be(new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Utc_is_unchanged_and_local_is_converted()
    {
        var utc = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);
        utc.AsUtc().Should().Be(utc);

        var local = utc.ToLocalTime();
        local.AsUtc().Should().Be(utc);
        local.AsUtc().Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Offsets_are_normalized_to_zero_and_nulls_stay_null()
    {
        DateTimeOffset? cairo = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(2));

        var utc = cairo.AsUtc()!.Value;

        utc.Offset.Should().Be(TimeSpan.Zero);
        utc.Should().Be(cairo.Value);
        ((DateTimeOffset?)null).AsUtc().Should().BeNull();
        ((DateTime?)null).AsUtc().Should().BeNull();
    }
}
