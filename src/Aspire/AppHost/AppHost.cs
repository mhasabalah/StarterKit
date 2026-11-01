using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

//var docker = builder.AddDockerComposeEnvironment("docker-env");

// ============================================================
// 1. Infrastructure Services
// ============================================================
var postgres = builder
    .AddPostgres("postgres", port: 5432)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithPgAdmin(c => c.WithLifetime(ContainerLifetime.Session));

var appDb = postgres.AddDatabase("starterkit");

var redis = builder
    .AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithRedisCommander(c => c.WithLifetime(ContainerLifetime.Session));

var seq = builder.AddSeq("seq")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithEnvironment("ACCEPT_EULA", "Y");

// ============================================================
// 2. Observability Services
// ============================================================

// Jaeger - Distributed Tracing UI
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one")
    .WithEndpoint(
        6831,
        6831,
        name: "agent",
        protocol: ProtocolType.Udp,
        isProxied: true,
        isExternal: false)
    .WithEndpoint(16686, 16686, name: "http", isProxied: true, isExternal: true)
    .WithEndpoint(14268, 14268, name: "collector", isProxied: true, isExternal: false)
    .WithEndpoint(14317, 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(14318, 4318, name: "otlp-http", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    jaeger.WithLifetime(ContainerLifetime.Persistent);
}

// Zipkin - Alternative Tracing UI
var zipkin = builder.AddContainer("zipkin", "openzipkin/zipkin")
    .WithEndpoint(9411, 9411, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    zipkin.WithLifetime(ContainerLifetime.Persistent);
}

// OpenTelemetry Collector - Central telemetry hub
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithBindMount(
        "../../../infra/observability/configs/otel-collector-config.yaml",
        "/etc/otelcol-contrib/config.yaml",
        true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(11888, 1888, name: "otel-pprof", isProxied: true, isExternal: true)
    .WithEndpoint(8888, 8888, name: "otel-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(8889, 8889, name: "otel-exporter-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(13133, 13133, name: "otel-health", isProxied: true, isExternal: true)
    .WithEndpoint(4317, 4317, name: "otel-grpc", isProxied: true, isExternal: true)
    .WithEndpoint(4318, 4318, name: "otel-http", isProxied: true, isExternal: true)
    .WithEndpoint(55679, 55679, name: "otel-zpages", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    otelCollector.WithLifetime(ContainerLifetime.Persistent);
}

// Prometheus - Metrics storage and querying
var prometheus = builder.AddContainer("prometheus", "prom/prometheus")
    .WithBindMount("../../../infra/observability/configs/prometheus.yaml",
        "/etc/prometheus/prometheus.yml")
    .WithArgs(
        "--config.file=/etc/prometheus/prometheus.yml",
        "--storage.tsdb.path=/prometheus",
        "--web.console.libraries=/usr/share/prometheus/console_libraries",
        "--web.console.templates=/usr/share/prometheus/consoles",
        "--web.enable-remote-write-receiver")
    .WithEndpoint(9090, 9090, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    prometheus.WithLifetime(ContainerLifetime.Persistent);
}

// Grafana - Unified observability dashboard
var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithEnvironment("GF_INSTALL_PLUGINS", "grafana-clock-panel,grafana-simple-json-datasource")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
    .WithEnvironment("GF_FEATURE_TOGGLES_ENABLE", "traceqlEditor")
    .WithBindMount("../../../infra/observability/configs/grafana/provisioning",
        "/etc/grafana/provisioning")
    .WithBindMount("../../../infra/observability/configs/grafana/dashboards",
        "/var/lib/grafana/dashboards")
    .WithEndpoint(3000, 3000, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    grafana.WithLifetime(ContainerLifetime.Persistent);
}

// Tempo - Grafana's distributed tracing backend
var tempo = builder.AddContainer("tempo", "grafana/tempo")
    .WithBindMount("../../../infra/observability/configs/tempo.yaml", "/etc/tempo.yaml", true)
    .WithArgs("--config.file=/etc/tempo.yaml")
    .WithEndpoint(3200, 3200, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(9095, 9095, name: "grpc", isProxied: true, isExternal: false)
    .WithEndpoint(4317, 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(4318, 4318, name: "otlp-http", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    tempo.WithLifetime(ContainerLifetime.Persistent);
}

// Loki - Log aggregation system
var loki = builder.AddContainer("loki", "grafana/loki")
    .WithBindMount("../../../infra/observability/configs/loki-config.yaml",
        "/etc/loki/local-config.yaml", true)
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithEndpoint(3100, 3100, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(9096, 9096, name: "grpc", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    loki.WithLifetime(ContainerLifetime.Persistent);
}

// ============================================================
// 3. Application
// ============================================================
var api = builder
    .AddProject<Projects.Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(seq)
    .WaitFor(seq);

if (builder.Environment.IsProduction())
{
    api.WithReplicas(2)
        .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
}

builder.Build().Run();
