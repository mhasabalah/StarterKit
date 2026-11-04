using System.Diagnostics;
using System.Diagnostics.Metrics;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.CQRS;
using Shared.Core.OpenTelemetryCollector;
using Shared.Core.OpenTelemetryCollector.Behaviors;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;

namespace Shared.Core.Tests.Observability;

/// <summary>
/// mediator request telemetry: one span + one duration measurement per request, correct command/query
/// classification, error status on failure, and never touching the caller's span when tracing is off.
/// </summary>
public sealed class RequestTelemetryBehaviorTests : IDisposable
{
    private readonly FakeDiagnostics _diagnostics = new($"tests.{Guid.NewGuid():N}");
    private readonly List<Activity> _activities = [];
    private readonly List<(double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public RequestTelemetryBehaviorTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _diagnostics.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _activities.Add(a)
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _diagnostics.Meter)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            _measurements.Add((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meterListener.Start();
    }

    private RequestTelemetryBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>() where TRequest : notnull =>
        new(_diagnostics, new RequestTelemetryMetrics(_diagnostics));

    [Fact]
    public async Task Command_success_creates_one_span_and_records_a_success_measurement()
    {
        var result = await Behavior<CreateThingCommand, Guid>()
            .Handle(new CreateThingCommand(), _ => Task.FromResult(Guid.Empty), CancellationToken.None);

        result.Should().Be(Guid.Empty);
        var span = _activities.Should().ContainSingle().Subject;
        span.DisplayName.Should().Be($"{_diagnostics.InstrumentationName}.command {nameof(CreateThingCommand)}");
        span.Status.Should().Be(ActivityStatusCode.Ok);
        span.GetTagItem(RequestTelemetryMetrics.KindTag).Should().Be("command");
        span.GetTagItem(RequestTelemetryMetrics.NameTag).Should().Be(nameof(CreateThingCommand));

        var measurement = _measurements.Should().ContainSingle().Subject;
        measurement.Value.Should().BeGreaterThanOrEqualTo(0);
        measurement.Tags[RequestTelemetryMetrics.OutcomeTag].Should().Be(RequestTelemetryMetrics.Success);
        measurement.Tags[RequestTelemetryMetrics.KindTag].Should().Be("command");
    }

    [Fact]
    public async Task Query_is_classified_as_query()
    {
        await Behavior<GetThingsQuery, string>()
            .Handle(new GetThingsQuery(), _ => Task.FromResult("ok"), CancellationToken.None);

        _activities.Should().ContainSingle().Which.GetTagItem(RequestTelemetryMetrics.KindTag).Should().Be("query");
    }

    [Fact]
    public async Task Failure_marks_the_span_as_error_records_failure_and_rethrows()
    {
        var act = () => Behavior<CreateThingCommand, Guid>()
            .Handle(new CreateThingCommand(), _ => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var span = _activities.Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.Events.Should().Contain(e => e.Name == "exception");
        _measurements.Should().ContainSingle()
            .Which.Tags[RequestTelemetryMetrics.OutcomeTag].Should().Be(RequestTelemetryMetrics.Failure);
    }

    [Fact]
    public async Task Cancellation_is_recorded_as_canceled_not_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Behavior<CreateThingCommand, Guid>()
            .Handle(new CreateThingCommand(), _ => throw new OperationCanceledException(cts.Token), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _measurements.Should().ContainSingle()
            .Which.Tags[RequestTelemetryMetrics.OutcomeTag].Should().Be(RequestTelemetryMetrics.Canceled);
    }

    [Fact]
    public async Task Without_a_listener_no_span_is_created_and_the_parent_span_keeps_running()
    {
        _activityListener.Dispose(); // tracing off for this source
        using var parentSource = new ActivitySource($"parent.{Guid.NewGuid():N}");
        using var parentListener = new ActivityListener
        {
            ShouldListenTo = s => s == parentSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(parentListener);
        using var parent = parentSource.StartActivity("http request")!;

        await Behavior<CreateThingCommand, Guid>()
            .Handle(new CreateThingCommand(), _ => Task.FromResult(Guid.Empty), CancellationToken.None);

        _activities.Should().BeEmpty();
        parent.IsStopped.Should().BeFalse("the behavior must never dispose the caller's Activity.Current");
        Activity.Current.Should().BeSameAs(parent);
    }

    [Fact]
    public void AddCustomObservability_registers_the_behavior_as_a_pipeline_behavior()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ObservabilityOptions:InstrumentationName"] = "test_service",
            ["ObservabilityOptions:UsePrometheusExporter"] = "false",
        });

        builder.AddCustomObservability();

        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(RequestTelemetryBehavior<,>));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(RequestTelemetryMetrics));
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
        _diagnostics.Dispose();
    }

    public sealed record CreateThingCommand : ICommand<Guid>;

    public sealed record GetThingsQuery : IQuery<string>;

    private sealed class FakeDiagnostics(string name) : IDiagnosticsProvider
    {
        public string InstrumentationName { get; } = name;
        public ActivitySource ActivitySource { get; } = new(name);
        public Meter Meter { get; } = new(name);

        public Task ExecuteActivityAsync(CreateActivityInfo createActivityInfo,
            Func<Activity?, CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteActivityAsync<TResult>(CreateActivityInfo createActivityInfo,
            Func<Activity?, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
            ActivitySource.Dispose();
            Meter.Dispose();
        }
    }
}
