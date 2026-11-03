using Microsoft.Extensions.Configuration;

namespace Shared.Core.OpenTelemetryCollector;

/// <summary>
/// Which OTLP exporters the application registers. Pure function of configuration so the rules are testable.
/// <list type="bullet">
/// <item><b>Standard</b>: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (Aspire injects its dashboard here, docker
/// compose the collector). The SDK reads endpoint, protocol and headers (e.g. the dashboard API key) from the
/// standard <c>OTEL_EXPORTER_OTLP_*</c> variables - nothing is hard-coded.</item>
/// <item><b>Collector</b>: <c>ObservabilityOptions:UseOTLPExporter</c> adds a SECOND destination
/// (<c>OTLPOptions:OTLPGrpcExporterEndpoint</c>), e.g. the full Grafana stack next to the Aspire dashboard.
/// Skipped when it is the same endpoint as the standard one, so telemetry is never exported twice.</item>
/// <item><b>Jaeger</b>: direct OTLP export to Jaeger (<c>UseJaegerExporter</c>), same de-duplication.</item>
/// </list>
/// Every exporter gets its own options NAME: unnamed <c>AddOtlpExporter</c> calls share one options instance, so
/// several of them would all silently export to the last configured endpoint.
/// </summary>
public sealed record OtlpExporterPlan(bool UseStandard, Uri? CollectorEndpoint, Uri? JaegerEndpoint)
{
    public const string StandardEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string CollectorExporterName = "collector";
    public const string JaegerExporterName = "jaeger";

    public bool HasAny => UseStandard || CollectorEndpoint is not null || JaegerEndpoint is not null;

    public static OtlpExporterPlan Create(ObservabilityOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var standard = ParseEndpoint(configuration[StandardEndpointVariable], StandardEndpointVariable);

        var collector = options.UseOTLPExporter
            ? ParseEndpoint(options.OTLPOptions?.OTLPGrpcExporterEndpoint, "ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint")
            : null;

        var jaeger = options.UseJaegerExporter
            ? ParseEndpoint(options.JaegerOptions?.OTLPGrpcExporterEndpoint, "ObservabilityOptions:JaegerOptions:OTLPGrpcExporterEndpoint")
            : null;

        if (SameEndpoint(collector, standard))
            collector = null;
        if (SameEndpoint(jaeger, standard) || SameEndpoint(jaeger, collector))
            jaeger = null;

        return new OtlpExporterPlan(standard is not null, collector, jaeger);
    }

    private static Uri? ParseEndpoint(string? value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new InvalidOperationException($"{setting} must be an absolute http(s) URL (got '{value}').");
    }

    private static bool SameEndpoint(Uri? left, Uri? right) =>
        left is not null && right is not null &&
        Uri.Compare(left, right, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
