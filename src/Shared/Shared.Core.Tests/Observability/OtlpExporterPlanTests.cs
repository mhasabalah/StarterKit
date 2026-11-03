using Microsoft.Extensions.Configuration;
using Shared.Core.OpenTelemetryCollector;

namespace Shared.Core.Tests.Observability;

public class OtlpExporterPlanTests
{
    private static IConfiguration Config(string? standardEndpoint) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OtlpExporterPlan.StandardEndpointVariable] = standardEndpoint
            })
            .Build();

    private static ObservabilityOptions Options(bool collector = false, string collectorEndpoint = "http://localhost:4317",
        bool jaeger = false, string jaegerEndpoint = "http://localhost:14317") => new()
    {
        InstrumentationName = "test",
        UseOTLPExporter = collector,
        OTLPOptions = new OTLPOptions { OTLPGrpcExporterEndpoint = collectorEndpoint },
        UseJaegerExporter = jaeger,
        JaegerOptions = new JaegerOptions { OTLPGrpcExporterEndpoint = jaegerEndpoint }
    };

    [Fact]
    public void Nothing_configured_exports_nowhere()
    {
        var plan = OtlpExporterPlan.Create(Options(), Config(null));

        plan.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Standard_endpoint_alone_uses_the_sdk_defaults()
    {
        // Aspire: OTEL_EXPORTER_OTLP_ENDPOINT = dashboard; the API must export there without any app setting.
        var plan = OtlpExporterPlan.Create(Options(), Config("https://localhost:21186"));

        plan.Should().Be(new OtlpExporterPlan(UseStandard: true, CollectorEndpoint: null, JaegerEndpoint: null));
    }

    [Fact]
    public void Collector_is_a_second_destination_next_to_the_standard_endpoint()
    {
        // Aspire full stack: dashboard (standard) + Grafana collector (explicit).
        var plan = OtlpExporterPlan.Create(
            Options(collector: true, collectorEndpoint: "http://localhost:51234"),
            Config("https://localhost:21186"));

        plan.UseStandard.Should().BeTrue();
        plan.CollectorEndpoint.Should().Be(new Uri("http://localhost:51234"));
    }

    [Theory]
    [InlineData("http://otel-collector:4317", "http://otel-collector:4317")]
    [InlineData("http://otel-collector:4317/", "http://OTEL-COLLECTOR:4317")]
    public void Collector_equal_to_the_standard_endpoint_is_not_exported_twice(string standard, string collector)
    {
        // docker compose: OTEL_EXPORTER_OTLP_ENDPOINT already points at the collector.
        var plan = OtlpExporterPlan.Create(Options(collector: true, collectorEndpoint: collector), Config(standard));

        plan.UseStandard.Should().BeTrue();
        plan.CollectorEndpoint.Should().BeNull();
    }

    [Fact]
    public void Collector_without_a_standard_endpoint_still_exports()
    {
        var plan = OtlpExporterPlan.Create(Options(collector: true), Config(null));

        plan.UseStandard.Should().BeFalse();
        plan.CollectorEndpoint.Should().Be(new Uri("http://localhost:4317"));
    }

    [Fact]
    public void Disabled_collector_ignores_its_endpoint()
    {
        var plan = OtlpExporterPlan.Create(Options(collector: false, collectorEndpoint: "not a url"), Config(null));

        plan.CollectorEndpoint.Should().BeNull();
    }

    [Fact]
    public void Jaeger_equal_to_the_collector_is_dropped()
    {
        var plan = OtlpExporterPlan.Create(
            Options(collector: true, collectorEndpoint: "http://localhost:4317", jaeger: true, jaegerEndpoint: "http://localhost:4317"),
            Config(null));

        plan.CollectorEndpoint.Should().NotBeNull();
        plan.JaegerEndpoint.Should().BeNull();
    }

    [Fact]
    public void Jaeger_on_its_own_endpoint_is_kept()
    {
        var plan = OtlpExporterPlan.Create(Options(jaeger: true), Config("https://localhost:21186"));

        plan.JaegerEndpoint.Should().Be(new Uri("http://localhost:14317"));
    }

    [Theory]
    [InlineData("localhost:4317")]
    [InlineData("ftp://collector:4317")]
    [InlineData("not a url")]
    public void Invalid_endpoint_fails_with_the_setting_name(string endpoint)
    {
        var act = () => OtlpExporterPlan.Create(Options(collector: true, collectorEndpoint: endpoint), Config(null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*OTLPOptions:OTLPGrpcExporterEndpoint*");
    }

    [Fact]
    public void Invalid_standard_endpoint_fails_with_the_variable_name()
    {
        var act = () => OtlpExporterPlan.Create(Options(), Config("collector:4317"));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{OtlpExporterPlan.StandardEndpointVariable}*");
    }
}
