using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Core.Behaviors;
using Shared.Contracts.Events;
using Shared.Core.DDD;
using Shared.Core.Tests.Infrastructure;
using Shared.Contracts.Messaging;

namespace Shared.Core.Tests.Events;

public sealed class EventMetadataTests
{
    private sealed record SomethingHappenedEvent(Guid AggregateId) : IDomainEvent;

    private sealed class SomethingIntegrationEvent : IntegrationEvent;

    [Fact]
    public void Domain_event_id_and_timestamp_are_stable_across_reads()
    {
        IDomainEvent e = new SomethingHappenedEvent(Guid.NewGuid());

        var id = e.EventId;
        var at = e.OccurredOn;
        Thread.Sleep(5);

        e.EventId.Should().Be(id).And.NotBe(Guid.Empty);
        e.OccurredOn.Should().Be(at);
        e.OccurredOn.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Value_equal_domain_events_still_get_distinct_ids()
    {
        var aggregateId = Guid.NewGuid();
        IDomainEvent a = new SomethingHappenedEvent(aggregateId);
        IDomainEvent b = new SomethingHappenedEvent(aggregateId);

        a.Should().Be(b); // record value equality
        a.EventId.Should().NotBe(b.EventId);
    }

    [Fact]
    public void Integration_event_id_and_timestamp_are_fixed_at_construction_in_utc()
    {
        var e = new SomethingIntegrationEvent();

        var id = e.EventId;
        var at = e.OccurredOn;
        Thread.Sleep(5);

        e.EventId.Should().Be(id).And.NotBe(Guid.Empty);
        e.OccurredOn.Should().Be(at);
        e.OccurredOn.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Integration_event_metadata_survives_serialization_round_trip()
    {
        var e = new SomethingIntegrationEvent();

        var copy = System.Text.Json.JsonSerializer.Deserialize<SomethingIntegrationEvent>(
            System.Text.Json.JsonSerializer.Serialize(e))!;

        copy.EventId.Should().Be(e.EventId);
        copy.OccurredOn.Should().Be(e.OccurredOn);
    }
}

public sealed class LoggingBehaviorTests
{
    private sealed record PingQuery : IRequest<string>;

    private static async Task<List<LogEntry>> RunAsync(TimeSpan handlerDuration)
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
        var behavior = new LoggingBehavior<PingQuery, string>(factory.CreateLogger<LoggingBehavior<PingQuery, string>>());

        await behavior.Handle(new PingQuery(), async _ =>
        {
            await Task.Delay(handlerDuration);
            return "pong";
        }, CancellationToken.None);

        return provider.Entries.ToList();
    }

    [Fact]
    public async Task Elapsed_time_is_logged_in_milliseconds()
    {
        var logs = await RunAsync(TimeSpan.FromMilliseconds(120));

        var end = logs.Single(l => l.Message.StartsWith("[END]"));
        Convert.ToInt64(end.State["ElapsedMilliseconds"]).Should().BeGreaterThanOrEqualTo(100).And.BeLessThan(3000);
        logs.Should().NotContain(l => l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Requests_slower_than_three_seconds_are_flagged()
    {
        // The old code compared TimeSpan.Seconds (> 3), so 3.0–3.99 s was never flagged.
        var logs = await RunAsync(TimeSpan.FromMilliseconds(3150));

        var warning = logs.Should().ContainSingle(l => l.Level == LogLevel.Warning).Subject;
        Convert.ToInt64(warning.State["ElapsedMilliseconds"]).Should().BeGreaterThanOrEqualTo(3000);
    }
}
