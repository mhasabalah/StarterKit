using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.OpenTelemetryCollector.Behaviors;
using Grafana.OpenTelemetry;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Core.Extensions;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;
using HealthEndpoints = Shared.Core.HealthCheck.Extensions;

namespace Shared.Core.OpenTelemetryCollector;

/// <summary>
/// OpenTelemetry logs, metrics and traces. Exporter selection: <see cref="OtlpExporterPlan"/>.
/// Resource identity: <see cref="ObservabilityOptions.ServiceName"/> (or the application name) unless the platform
/// sets <c>OTEL_SERVICE_NAME</c> / <c>OTEL_RESOURCE_ATTRIBUTES</c> (Aspire, docker compose) - those win, so the
/// Aspire dashboard links telemetry to the right resource and replica.
/// </summary>
public static class Extensions
{
    public static WebApplicationBuilder AddCustomObservability(this WebApplicationBuilder builder)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        builder.Services.AddSingleton<IDiagnosticsProvider, CustomeDiagnosticsProvider>();
        builder.AddCoreDiagnostics();

        builder.Services.AddValidateOptions<ObservabilityOptions>();
        var observabilityOptions =
            builder.Services.GetOptions<ObservabilityOptions>(nameof(ObservabilityOptions));

        // InstrumentationName property option is mandatory and can't be empty
        ArgumentException.ThrowIfNullOrEmpty(observabilityOptions.InstrumentationName);
        ObservabilityConstant.InstrumentationName = observabilityOptions.InstrumentationName;

        if (observabilityOptions is
            { MetricsEnabled: false, TracingEnabled: false, LoggingEnabled: false })
        {
            return builder;
        }

        var exporterPlan = OtlpExporterPlan.Create(observabilityOptions, builder.Configuration);
        var serviceName = observabilityOptions.ServiceName ?? builder.Environment.ApplicationName;
        var serviceVersion = ServiceVersion();
        var environmentName = builder.Environment.EnvironmentName;

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment.name", environmentName),
                    new KeyValuePair<string, object>("service.environment", environmentName)
                ])
                // Added last so platform-provided OTEL_SERVICE_NAME / OTEL_RESOURCE_ATTRIBUTES override the defaults above.
                .AddEnvironmentVariableDetector());

        if (observabilityOptions.LoggingEnabled)
        {
            otel.WithLogging(
                logging => AddLoggingExporters(logging, observabilityOptions, exporterPlan),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    // Parse the state passed to logger.Log even when it is not a KeyValuePair collection.
                    options.ParseStateValues = true;

                    if (observabilityOptions.UseGrafanaExporter)
                    {
                        options.UseGrafana();
                    }
                });
        }

        if (observabilityOptions.MetricsEnabled)
        {
            otel.WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddProcessInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(InstrumentationOptions.MeterName)
                    .AddMeter(observabilityOptions.InstrumentationName)
                    .AddView(
                        "http.server.request.duration",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries =
                            [
                                0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5,
                                7.5, 10
                            ]
                        }
                    )
                    .AddMeter("System.Runtime")
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel");

                AddMetricsExporters(metrics, observabilityOptions, exporterPlan);
            });
        }

        if (observabilityOptions.TracingEnabled)
        {
            otel.WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // We want to view all traces in development
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                tracing
                    .SetErrorStatusOnException()
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        // Health probes would otherwise flood the trace view.
                        options.Filter = httpContext =>
                            !(httpContext.Request.Path.StartsWithSegments(
                                  HealthEndpoints.HealthEndpointPath, StringComparison.OrdinalIgnoreCase) ||
                              httpContext.Request.Path.StartsWithSegments(
                                  HealthEndpoints.AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase));
                    })
                    .AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation(instrumentationOptions =>
                    {
                        instrumentationOptions.RecordException = true;
                    })
                    // OTel 1.20+: SQL text (db.query.text) is captured by default; the old
                    // SetDbStatementForText option was removed with the stable DB semantic conventions.
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource(DiagnosticHeaders.DefaultListenerName)
                    .AddNpgsql()
                    // `AddSource` for adding custom activity sources
                    .AddSource(observabilityOptions.InstrumentationName)
                    // Transactional outbox publish spans (parented on the flow that raised the event).
                    .AddSource(Shared.Core.Messaging.Outbox.OutboxRelay.ActivitySourceName)
                    .AddSource("Microsoft.AspNetCore.Hosting")
                    .AddSource("Microsoft.AspNetCore.Server.Kestrel");

                AddTracingExporters(tracing, observabilityOptions, exporterPlan);
            });
        }

        return builder;
    }

    public static WebApplication UseCustomObservability(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        app.Use(async (context, next) =>
            {
                var metricsFeature = context.Features.Get<IHttpMetricsTagsFeature>();
                if (metricsFeature != null && context.Request.Path is
                    { Value: "/metrics" or HealthEndpoints.HealthEndpointPath or HealthEndpoints.AlivenessEndpointPath })
                {
                    metricsFeature.MetricsDisabled = true;
                }

                await next(context);
            }
        );

        if (options.UsePrometheusExporter)
        {
            // Application metrics on `/metrics` for a Prometheus scrape job.
            app.UseOpenTelemetryPrometheusScrapingEndpoint();
        }

        return app;
    }

    private static void AddTracingExporters(
        TracerProviderBuilder tracing, ObservabilityOptions options, OtlpExporterPlan plan)
    {
        if (plan.UseStandard)
        {
            tracing.AddOtlpExporter();
        }

        if (plan.CollectorEndpoint is { } collector)
        {
            tracing.AddOtlpExporter(OtlpExporterPlan.CollectorExporterName, o => UseGrpc(o, collector));
        }

        if (plan.JaegerEndpoint is { } jaeger)
        {
            tracing.AddOtlpExporter(OtlpExporterPlan.JaegerExporterName, o => UseGrpc(o, jaeger));
        }

        if (options.UseZipkinExporter)
        {
            tracing.AddZipkinExporter(x => x.Endpoint = new Uri(options.ZipkinOptions.HttpExporterEndpoint));
        }

        if (options.UseConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }

        if (options.UseGrafanaExporter)
        {
            // https://github.com/grafana/grafana-opentelemetry-dotnet/blob/main/docs/configuration.md
            // Note: the Grafana distro exports via the standard OTEL_EXPORTER_OTLP_* settings itself - enable it
            // INSTEAD of a standard endpoint, not in addition, or spans are exported twice.
            tracing.UseGrafana();
        }
    }

    private static void AddMetricsExporters(
        MeterProviderBuilder metrics, ObservabilityOptions options, OtlpExporterPlan plan)
    {
        if (options.UsePrometheusExporter)
        {
            metrics.AddPrometheusExporter(o => o.DisableTotalNameSuffixForCounters = true);
        }

        if (plan.UseStandard)
        {
            metrics.AddOtlpExporter();
        }

        if (plan.CollectorEndpoint is { } collector)
        {
            metrics.AddOtlpExporter(OtlpExporterPlan.CollectorExporterName, o => UseGrpc(o, collector));
        }

        if (options.UseConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }

        if (options.UseGrafanaExporter)
        {
            metrics.UseGrafana();
        }
    }

    private static void AddLoggingExporters(
        LoggerProviderBuilder logging, ObservabilityOptions options, OtlpExporterPlan plan)
    {
        if (plan.UseStandard)
        {
            logging.AddOtlpExporter();
        }

        if (plan.CollectorEndpoint is { } collector)
        {
            logging.AddOtlpExporter(OtlpExporterPlan.CollectorExporterName, o => UseGrpc(o, collector));
        }

        if (options.UseConsoleExporter)
        {
            logging.AddConsoleExporter();
        }
    }

    private static void UseGrpc(OtlpExporterOptions options, Uri endpoint)
    {
        options.Endpoint = endpoint;
        options.Protocol = OtlpExportProtocol.Grpc;
    }

    private static string ServiceVersion()
    {
        var entry = Assembly.GetEntryAssembly();
        return entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? entry?.GetName().Version?.ToString()
               ?? "unknown";
    }

    /// <summary>
    /// mediator request telemetry (span + duration histogram per command/query). Registered here - before any module's
    /// <c>AddMediator</c> - so it is the OUTERMOST pipeline behavior, and only in the host (module test fixtures that do
    /// not add observability stay unaffected).
    /// </summary>
    private static WebApplicationBuilder AddCoreDiagnostics(this WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton<RequestTelemetryMetrics>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(RequestTelemetryBehavior<,>)));

        return builder;
    }
}
