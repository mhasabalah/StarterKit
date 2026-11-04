using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;

namespace Shared.Core.OpenTelemetryCollector.Behaviors;

/// <summary>
/// One span and one duration measurement per MediatR request (command / query / other), as the OUTERMOST behavior
/// (registered by <c>AddCustomObservability</c>, before any module's <c>AddMediatR</c>), so validation, concurrency
/// handling and domain-event dispatch are inside the span.
/// <list type="bullet">
/// <item>Span: <c>{InstrumentationName}.{kind} {RequestName}</c> on the shared <see cref="IDiagnosticsProvider.ActivitySource"/>
/// (already exported by the tracer), tags <c>mediatr.request.name</c> / <c>mediatr.request.kind</c>; error status +
/// exception event on failure. No span is created when nobody listens (tracing disabled) - the parent span is never touched.</item>
/// <item>Metric: histogram <c>mediatr.request.duration</c> (seconds) tagged with name, kind and outcome
/// (<c>success</c> / <c>failure</c> / <c>canceled</c>). Low cardinality: request TYPE names only, never ids.</item>
/// </list>
/// Host-only: module test fixtures that call <c>Add&lt;Module&gt;Module</c> without observability are unaffected.
/// </summary>
public sealed class RequestTelemetryBehavior<TRequest, TResponse>(
    IDiagnosticsProvider diagnostics,
    RequestTelemetryMetrics metrics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var (name, kind) = RequestTelemetryMetrics.Describe(typeof(TRequest));
        var started = Stopwatch.GetTimestamp();

        using var activity = diagnostics.ActivitySource.StartActivity(
            $"{diagnostics.InstrumentationName}.{kind} {name}", ActivityKind.Internal);
        activity?.SetTag(RequestTelemetryMetrics.NameTag, name);
        activity?.SetTag(RequestTelemetryMetrics.KindTag, kind);

        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            metrics.Record(name, kind, RequestTelemetryMetrics.Success, Stopwatch.GetElapsedTime(started));
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            metrics.Record(name, kind, RequestTelemetryMetrics.Canceled, Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.AddException(ex);
            metrics.Record(name, kind, RequestTelemetryMetrics.Failure, Stopwatch.GetElapsedTime(started));
            throw;
        }
    }
}

/// <summary>Instruments for <see cref="RequestTelemetryBehavior{TRequest,TResponse}"/> (singleton: created once).</summary>
public sealed class RequestTelemetryMetrics
{
    public const string DurationInstrument = "mediatr.request.duration";
    public const string NameTag = "mediatr.request.name";
    public const string KindTag = "mediatr.request.kind";
    public const string OutcomeTag = "mediatr.request.outcome";
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Canceled = "canceled";

    private static readonly ConcurrentDictionary<Type, (string Name, string Kind)> Descriptions = new();

    private readonly Histogram<double> _duration;

    public RequestTelemetryMetrics(IDiagnosticsProvider diagnostics) =>
        _duration = diagnostics.Meter.CreateHistogram<double>(
            DurationInstrument, unit: "s", description: "Duration of MediatR requests (commands and queries)");

    public void Record(string name, string kind, string outcome, TimeSpan elapsed)
    {
        if (!_duration.Enabled)
        {
            return;
        }

        _duration.Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>(NameTag, name),
            new KeyValuePair<string, object?>(KindTag, kind),
            new KeyValuePair<string, object?>(OutcomeTag, outcome));
    }

    /// <summary>Request type name and kind, computed once per type (no per-request reflection).</summary>
    public static (string Name, string Kind) Describe(Type requestType) =>
        Descriptions.GetOrAdd(requestType, static type =>
        {
            var kind = Implements(type, typeof(ICommand<>)) ? "command"
                : Implements(type, typeof(IQuery<>)) ? "query"
                : "request";
            return (type.Name, kind);
        });

    private static bool Implements(Type type, Type openGeneric) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);
}
