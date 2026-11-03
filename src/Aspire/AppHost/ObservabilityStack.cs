using Aspire.Hosting.ApplicationModel;

namespace AppHost;

/// <summary>
/// Optional Grafana observability stack (OTel Collector → Tempo + Jaeger / Prometheus / Loki → Grafana), the same
/// topology and config files as <c>docker-compose.observability.yml</c> (<c>infra/observability/configs</c>).
/// <para>
/// Not needed for everyday work: the Aspire dashboard already shows the API's logs, traces and metrics.
/// Enable it with <c>Observability:FullStack=true</c> to work on dashboards, alerts or the collector pipeline.
/// </para>
/// Design rules:
/// <list type="bullet">
/// <item>Images are pinned - a floating <c>latest</c> breaks a dev machine without any code change.</item>
/// <item>No fixed host ports: containers reach each other by resource name on the Aspire network, and the
/// host-facing ports are allocated by Aspire, so nothing can collide with local services or other stacks.</item>
/// <item>Every container has a health check, so the dashboard shows real readiness instead of "Running".</item>
/// </list>
/// </summary>
public static class ObservabilityStack
{
    // Pinned images (bump deliberately, together with infra/observability/configs).
    public const string CollectorImage = "otel/opentelemetry-collector-contrib";
    public const string CollectorTag = "0.144.0";
    public const string PrometheusImage = "prom/prometheus";
    public const string PrometheusTag = "v3.5.1";
    public const string TempoImage = "grafana/tempo";
    public const string TempoTag = "2.8.3";
    public const string LokiImage = "grafana/loki";
    public const string LokiTag = "3.6.4";
    public const string JaegerImage = "jaegertracing/all-in-one";
    public const string JaegerTag = "1.76.0";
    public const string GrafanaImage = "grafana/grafana";
    public const string GrafanaTag = "12.3.1";

    public const string CollectorName = "otel-collector";
    public const string CollectorOtlpGrpcEndpoint = "otlp-grpc";

    /// <summary>Adds the stack and returns the collector; the caller points the API at its OTLP endpoint.</summary>
    public static IResourceBuilder<ContainerResource> AddObservabilityStack(this IDistributedApplicationBuilder builder)
    {
        var configs = ConfigDirectory(builder);

        // Names must match the host names used in infra/observability/configs/*.yaml.
        var tempo = builder.AddContainer("tempo", TempoImage, TempoTag)
            .WithBindMount(Path.Combine(configs, "tempo.yaml"), "/etc/tempo.yaml", isReadOnly: true)
            .WithArgs("-config.file=/etc/tempo.yaml")
            .WithHttpEndpoint(targetPort: 3200, name: "http")
            .WithHttpHealthCheck("/ready", endpointName: "http");

        var loki = builder.AddContainer("loki", LokiImage, LokiTag)
            .WithBindMount(Path.Combine(configs, "loki-config.yaml"), "/etc/loki/local-config.yaml", isReadOnly: true)
            .WithArgs("-config.file=/etc/loki/local-config.yaml")
            .WithHttpEndpoint(targetPort: 3100, name: "http")
            .WithHttpHealthCheck("/ready", endpointName: "http");

        var jaeger = builder.AddContainer("jaeger", JaegerImage, JaegerTag)
            .WithHttpEndpoint(targetPort: 16686, name: "http")
            .WithHttpHealthCheck("/", endpointName: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "Jaeger UI");

        var prometheus = builder.AddContainer("prometheus", PrometheusImage, PrometheusTag)
            .WithBindMount(Path.Combine(configs, "prometheus.yaml"), "/etc/prometheus/prometheus.yml", isReadOnly: true)
            .WithArgs(
                "--config.file=/etc/prometheus/prometheus.yml",
                "--storage.tsdb.path=/prometheus",
                "--web.enable-remote-write-receiver")
            .WithHttpEndpoint(targetPort: 9090, name: "http")
            .WithHttpHealthCheck("/-/ready", endpointName: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "Prometheus");

        var collector = builder.AddContainer(CollectorName, CollectorImage, CollectorTag)
            .WithBindMount(Path.Combine(configs, "otel-collector-config.yaml"), "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
            .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
            .WithEndpoint(targetPort: 4317, scheme: "http", name: CollectorOtlpGrpcEndpoint)
            .WithEndpoint(targetPort: 4318, scheme: "http", name: "otlp-http")
            .WithHttpEndpoint(targetPort: 13133, name: "health")
            .WithHttpHealthCheck("/", endpointName: "health")
            .WaitFor(tempo)
            .WaitFor(loki)
            .WaitFor(jaeger)
            .WaitFor(prometheus);

        // Generated once and persisted in the AppHost user secrets (Parameters:grafana-admin-password).
        var grafanaPassword = builder.AddParameter(
            "grafana-admin-password",
            new GenerateParameterDefault { MinLength = 20, Special = false },
            secret: true,
            persist: true);

        builder.AddContainer("grafana", GrafanaImage, GrafanaTag)
            .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", grafanaPassword)
            .WithBindMount(Path.Combine(configs, "grafana", "provisioning"), "/etc/grafana/provisioning", isReadOnly: true)
            .WithBindMount(Path.Combine(configs, "grafana", "dashboards"), "/var/lib/grafana/dashboards", isReadOnly: true)
            .WithHttpEndpoint(targetPort: 3000, name: "http")
            .WithHttpHealthCheck("/api/health", endpointName: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "Grafana")
            .WaitFor(prometheus)
            .WaitFor(tempo)
            .WaitFor(loki);

        return collector;
    }

    /// <summary>Absolute path of <c>infra/observability/configs</c>; fails fast when the repo layout changed.</summary>
    public static string ConfigDirectory(IDistributedApplicationBuilder builder)
    {
        var path = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "infra", "observability", "configs"));
        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException($"Observability configs not found at {path} (expected <repo>/infra/observability/configs).");
    }
}
