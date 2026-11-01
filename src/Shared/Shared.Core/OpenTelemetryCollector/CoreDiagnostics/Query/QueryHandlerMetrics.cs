using System.Diagnostics.Metrics;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;

namespace Shared.Core.OpenTelemetryCollector.CoreDiagnostics.Query;

public class QueryHandlerMetrics
{
    private readonly UpDownCounter<long> _activeQueriesCounter;
    private readonly Counter<long> _totalQueriesNumber;
    private readonly Counter<long> _successQueriesNumber;
    private readonly Counter<long> _failedQueriesNumber;
    private readonly Histogram<double> _handlerDuration;

    private Stopwatch _timer;

    public QueryHandlerMetrics(IDiagnosticsProvider diagnosticsProvider)
    {
        _activeQueriesCounter = diagnosticsProvider.Meter.CreateUpDownCounter<long>(
            TelemetryTags.Metrics.Application.Commands.ActiveCount,
            "{active_queries}",
            "Number of queries currently being handled"
        );

        _totalQueriesNumber = diagnosticsProvider.Meter.CreateCounter<long>(
            TelemetryTags.Metrics.Application.Commands.TotalExecutedCount,
            "{total_queries}",
            "Total number of executed query that sent to query handlers"
        );

        _successQueriesNumber = diagnosticsProvider.Meter.CreateCounter<long>(
            TelemetryTags.Metrics.Application.Commands.SuccessCount,
            "{success_queries}",
            "Number queries that handled successfully"
        );

        _failedQueriesNumber = diagnosticsProvider.Meter.CreateCounter<long>(
            TelemetryTags.Metrics.Application.Commands.FaildCount,
            "{failed_queries}",
            "Number queries that handled with errors"
        );

        _handlerDuration = diagnosticsProvider.Meter.CreateHistogram<double>(
            TelemetryTags.Metrics.Application.Commands.HandlerDuration,
            "s",
            "Measures the duration of query handler"
        );
    }

    public void StartExecuting<TQuery>()
    {
        var queryName = typeof(TQuery).Name;
        var handlerType = typeof(TQuery)
            .Assembly.GetTypes()
            .FirstOrDefault(t =>
                t.GetInterfaces()
                    .Any(i =>
                        i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                        && i.GetGenericArguments()[0] == typeof(TQuery)
                    )
            );
        var queryHandlerName = handlerType?.Name;

        var tags = new TagList
        {
            { TelemetryTags.Tracing.Application.Queries.Query, queryName },
            { TelemetryTags.Tracing.Application.Queries.QueryType, typeof(TQuery).FullName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandler, queryHandlerName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandlerType, handlerType?.FullName }
        };

        if (_activeQueriesCounter.Enabled)
        {
            _activeQueriesCounter.Add(1, tags);
        }

        if (_totalQueriesNumber.Enabled)
        {
            _totalQueriesNumber.Add(1, tags);
        }

        _timer = Stopwatch.StartNew();
    }

    public void FinishExecuting<TQuery>()
    {
        var queryName = typeof(TQuery).Name;
        var handlerType = typeof(TQuery)
            .Assembly.GetTypes()
            .FirstOrDefault(t =>
                t.GetInterfaces()
                    .Any(i =>
                        i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                        && i.GetGenericArguments()[0] == typeof(TQuery)
                    )
            );
        var queryHandlerName = handlerType?.Name;

        var tags = new TagList
        {
            { TelemetryTags.Tracing.Application.Queries.Query, queryName },
            { TelemetryTags.Tracing.Application.Queries.QueryType, typeof(TQuery).FullName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandler, queryHandlerName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandlerType, handlerType?.FullName }
        };

        if (_activeQueriesCounter.Enabled)
        {
            _activeQueriesCounter.Add(-1, tags);
        }

        if (!_handlerDuration.Enabled)
        {
            return;
        }

        var elapsedTimeSeconds = _timer.Elapsed.Seconds;

        _handlerDuration.Record(elapsedTimeSeconds, tags);

        if (_successQueriesNumber.Enabled)
        {
            _successQueriesNumber.Add(1, tags);
        }
    }

    public void FailedCommand<TQuery>()
    {
        var queryName = typeof(TQuery).Name;
        var handlerType = typeof(TQuery)
            .Assembly.GetTypes()
            .FirstOrDefault(t =>
                t.GetInterfaces()
                    .Any(i =>
                        i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                        && i.GetGenericArguments()[0] == typeof(TQuery)
                    )
            );
        var queryHandlerName = handlerType?.Name;

        var tags = new TagList
        {
            { TelemetryTags.Tracing.Application.Queries.Query, queryName },
            { TelemetryTags.Tracing.Application.Queries.QueryType, typeof(TQuery).FullName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandler, queryHandlerName },
            { TelemetryTags.Tracing.Application.Queries.QueryHandlerType, handlerType?.FullName }
        };

        if (_failedQueriesNumber.Enabled)
        {
            _failedQueriesNumber.Add(1, tags);
        }
    }
}
