namespace Shared.Core.OpenTelemetryCollector;

public class ObservabilityOptions
{
    public string InstrumentationName { get; set; } = default!;
    public string? ServiceName { get; set; }
    public bool MetricsEnabled { get; set; } = true;
    public bool TracingEnabled { get; set; } = true;
    public bool LoggingEnabled { get; set; } = true;
    public bool UsePrometheusExporter { get; set; } = true;

    /// <summary>
    /// Adds a second OTLP destination (<see cref="OTLPOptions"/>) next to the standard <c>OTEL_EXPORTER_OTLP_*</c>
    /// one - see <see cref="OtlpExporterPlan"/>. Off by default.
    /// </summary>
    public bool UseOTLPExporter { get; set; }
    public bool UseGrafanaExporter { get; set; }
    public bool UseConsoleExporter { get; set; }
    public bool UseJaegerExporter { get; set; }
    public bool UseZipkinExporter { get; set; }
    public ZipkinOptions ZipkinOptions { get; set; } = new();
    public JaegerOptions JaegerOptions { get; set; } = new();
    public OTLPOptions OTLPOptions { get; set; } = new();
}

// https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Zipkin/README.md
public class ZipkinOptions
{
    /// <summary>
    /// Gets or sets endpoint address to receive telemetry
    /// </summary>
    public string HttpExporterEndpoint { get; set; } = "http://localhost:9411/api/v2/spans";
}

public class JaegerOptions
{
    public string OTLPGrpcExporterEndpoint { get; set; } = "http://localhost:14317";
    public string HttpExporterEndpoint { get; set; } = "http://localhost:14268/api/traces";
}

public class OTLPOptions
{
    public string OTLPGrpcExporterEndpoint { get; set; } = "http://localhost:4317";
    public string OTLPHttpExporterEndpoint { get; set; } = "http://localhost:4318";
}

