using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using Shared.Core.OpenTelemetryCollector;

namespace Shared.Core.Tests.Observability;

/// <summary>
/// Real AddCustomObservability registration (no exporting happens: providers are never resolved).
/// Guards the bug where unnamed AddOtlpExporter calls shared ONE options instance, so every exporter silently
/// sent to the last configured endpoint.
/// </summary>
public class ObservabilityRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ObservabilityOptions:InstrumentationName"] = "test_service",
            ["ObservabilityOptions:UsePrometheusExporter"] = "false",
        }.Concat(settings));

        builder.AddCustomObservability();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void Standard_and_collector_exporters_keep_their_own_endpoints()
    {
        using var provider = Build(new()
        {
            [OtlpExporterPlan.StandardEndpointVariable] = "https://localhost:21186",
            ["ObservabilityOptions:UseOTLPExporter"] = "true",
            ["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"] = "http://localhost:51234",
        });

        var exporters = provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>();

        exporters.Get(Microsoft.Extensions.Options.Options.DefaultName).Endpoint
            .Should().Be(new Uri("https://localhost:21186"), "the standard exporter follows OTEL_EXPORTER_OTLP_ENDPOINT");
        exporters.Get(OtlpExporterPlan.CollectorExporterName).Endpoint
            .Should().Be(new Uri("http://localhost:51234"));
        exporters.Get(OtlpExporterPlan.CollectorExporterName).Protocol.Should().Be(OtlpExportProtocol.Grpc);
    }

    [Fact]
    public void Invalid_exporter_endpoint_fails_at_startup()
    {
        var act = () => Build(new()
        {
            ["ObservabilityOptions:UseOTLPExporter"] = "true",
            ["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"] = "collector:4317",
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*OTLPGrpcExporterEndpoint*");
    }

    [Fact]
    public void Missing_instrumentation_name_fails_at_startup()
    {
        var act = () => Build(new() { ["ObservabilityOptions:InstrumentationName"] = "" });

        act.Should().Throw<ArgumentException>();
    }
}
