# StarterKit Observability Stack Documentation

## 🎯 Overview

Complete observability solution for StarterKit using modern open-source tools integrated via OpenTelemetry.

## 📊 Components

| Component | Version | Ports | Purpose |
|-----------|---------|-------|---------|
| **OpenTelemetry Collector** | Contrib Latest | 4317 (gRPC), 4318 (HTTP) | Central telemetry hub and router |
| **Prometheus** | Latest | 9090 | Metrics storage and querying |
| **Grafana** | Latest | 3000 | Unified visualization dashboard |
| **Jaeger** | Latest | 16686 | Distributed tracing UI |
| **Tempo** | Latest | 3200, 9095 | Scalable distributed tracing backend |
| **Loki** | Latest | 3100 | Log aggregation system |
| **Zipkin** | Latest | 9411 | Alternative tracing backend |
| **Node Exporter** | Latest | 9100 | System-level metrics |
| **cAdvisor** | Latest | 8080 | Container metrics |

## 🚀 Quick Start

### 1. Start Full Observability Stack

```powershell
# Start all observability services
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d

# Include Jaeger (profile-gated in the base overlay)
docker compose -f docker-compose.yml -f docker-compose.observability.yml --profile jaeger up -d

# Production server
docker compose -f docker-compose.yml -f docker-compose.observability.yml --env-file .env.prod up -d
```

### 2. Access Dashboards

- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **Jaeger UI**: http://localhost:16686
- **Zipkin UI**: http://localhost:9411
- **Tempo**: http://localhost:3200
- **Loki**: http://localhost:3100
- **cAdvisor**: http://localhost:8080
- **OTEL Collector Health**: http://localhost:13133

## 📁 Configuration Files

### Directory Structure

```
infra/observability/
├── configs/
│   ├── otel-collector-config.yaml    # OpenTelemetry Collector
│   ├── prometheus.yaml                # Prometheus scrape config
│   ├── alerts.yaml                    # Prometheus alert rules (outbox dead letters / backlog)
│   ├── tempo.yaml                     # Tempo tracing config
│   ├── loki-config.yaml              # Loki log config
│   └── grafana/
│       ├── provisioning/
│       │   ├── datasources/
│       │   │   └── datasources.yml   # Auto-configure data sources
│       │   └── dashboards/
│       │       └── dashboards.yml    # Auto-provision dashboards
│       └── dashboards/               # JSON dashboard files
```

## 🔧 OpenTelemetry Collector

The OTEL Collector is the **central hub** that receives, processes, and exports all telemetry:

```
┌─────────────┐
│ StarterKit API │
└──────┬──────┘
       │ OTLP (4317/4318)
       v
┌──────────────────────┐
│  OTEL Collector      │
│  ┌─────────────────┐ │
│  │   Receivers     │ │ ← Receive telemetry
│  ├─────────────────┤ │
│  │   Processors    │ │ ← Process & batch
│  ├─────────────────┤ │
│  │   Exporters     │ │ ← Send to backends
│  └─────────────────┘ │
└──────────────────────┘
       │
       ├──→ Tempo (Traces)
       ├──→ Jaeger (Traces)
       ├──→ Zipkin (Traces)
       ├──→ Prometheus (Metrics)
       └──→ Loki (Logs)
```

### Key Features

- **Receivers**: OTLP gRPC and HTTP
- **Processors**: Batching, memory limiting
- **Exporters**: Multi-backend support
- **Extensions**: Health checks, profiling, zpages

## 📈 Prometheus

Metrics storage and querying engine.

### Scrape Targets

- OpenTelemetry Collector (8888, 8889)
- Prometheus itself
- Node Exporter (system metrics)
- cAdvisor (container metrics)
- Tempo (trace metrics)

### Key Features

- Remote write receiver enabled
- Exemplar storage enabled
- Native histograms support
- 15s scrape interval

## 📊 Grafana

Unified visualization dashboard connecting all data sources.

### Pre-configured Data Sources

1. **Prometheus** (default) - Metrics
2. **Tempo** - Traces with service graphs
3. **Loki** - Logs with trace correlation
4. **Jaeger** - Alternative trace UI
5. **Zipkin** - Alternative trace UI

### Trace-to-Log Correlation

Grafana automatically links:
- Traces → Logs (via `trace_id`)
- Traces → Metrics (via Prometheus)
- Logs → Traces (via derived fields)

## 🔍 Tempo

Scalable distributed tracing backend by Grafana.

### Features

- OTLP gRPC/HTTP receivers
- Service graph generation
- Metrics generation
- 48-hour trace retention
- Integration with Prometheus

## 📝 Loki

Log aggregation system optimized for Kubernetes and cloud-native apps.

### Features

- OTLP HTTP endpoint
- Structured metadata support
- Trace ID extraction
- 7-day log retention
- Query via LogQL

## 🐛 Jaeger

Production-ready distributed tracing system.

### Features

- OTLP enabled
- Full-text search
- Dependency graphs
- Service performance monitoring
- Prometheus metrics integration

## 🔬 Zipkin

Alternative distributed tracing system.

### Use Cases

- Legacy application support
- Simpler setup than Jaeger
- Compatible with Zipkin clients

## 📊 Node Exporter

System and hardware metrics for Prometheus.

### Metrics Collected

- CPU usage
- Memory usage
- Disk I/O
- Network statistics
- File system usage

## 📦 cAdvisor

Container resource usage and performance analysis.

### Metrics Collected

- Container CPU usage
- Container memory usage
- Container network I/O
- Container disk I/O
- Container resource limits

## 🛠️ .NET Integration

### 1. ServiceDefaults Configuration

Add to `ServiceDefaults/Extensions.cs`:

```csharp
public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddPrometheusExporter())
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddGrpcClientInstrumentation()
            .AddSource("MassTransit"))
        .UseOtlpExporter();

    return builder;
}
```

### 2. AppHost Configuration

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add observability stack references
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithHttpEndpoint(port: 4318, targetPort: 4318, name: "otlp-http")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc", scheme: "http");

var prometheus = builder.AddContainer("prometheus", "prom/prometheus")
    .WithHttpEndpoint(port: 9090, targetPort: 9090);

var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithHttpEndpoint(port: 3000, targetPort: 3000);

// Configure API to use OTEL Collector
var api = builder.AddProject<Projects.Api>("api")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318");
```

### 3. Environment Variables

Add to `.env` or `appsettings.json`:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=starterkit-api
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production,service.version=1.0.0
```

## 🎯 Usage Scenarios

### Scenario 1: Development with Minimal Observability

```powershell
# Start only Prometheus + Grafana
docker-compose -f docker-compose.observability.yml up prometheus grafana -d
```

### Scenario 2: Full Observability Stack

```powershell
# Start complete stack
docker-compose -f docker-compose.observability.yml --profile observability up -d
```

### Scenario 3: Production with Monitoring

```powershell
# Include monitoring tools
docker-compose -f docker-compose.observability.yml --profile observability --profile monitoring up -d
```

### Scenario 4: Traces Only (Tempo + Grafana)

```powershell
docker-compose -f docker-compose.observability.yml up tempo grafana -d
```

## 📊 Example Queries

### Prometheus (PromQL)

```promql
# Request rate
rate(http_requests_total[5m])

# Error rate
rate(http_requests_total{status=~"5.."}[5m])

# P99 latency
histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))

# Memory usage
process_resident_memory_bytes

# Active database connections
dbcp_connections_active
```

### Loki (LogQL)

```logql
# All logs for starterkit-api
{service_name="starterkit-api"}

# Error logs only
{service_name="starterkit-api"} |= "error"

# Logs for specific trace
{service_name="starterkit-api"} | trace_id="abc123def456"

# JSON field extraction
{service_name="starterkit-api"} | json | level="Error"
```

### Tempo (TraceQL)

```traceql
# Find slow traces
duration > 1s

# Find traces with errors
status = error

# Find traces for specific service
resource.service.name = "starterkit-api"

# Complex query
{ span.http.method = "POST" } && { duration > 100ms }
```

## 🔧 Troubleshooting

### OTEL Collector Not Receiving Data

```powershell
# Check collector health
curl http://localhost:13133

# View collector logs
docker logs starterkit-otel-collector -f

# Check zpages
Start-Process "http://localhost:55679/debug/tracez"
```

### Grafana Cannot Connect to Data Sources

```powershell
# Verify network connectivity
docker exec starterkit-grafana ping prometheus
docker exec starterkit-grafana curl http://prometheus:9090/-/healthy

# Check datasource provisioning
docker exec starterkit-grafana cat /etc/grafana/provisioning/datasources/datasources.yml
```

### No Metrics in Prometheus

```powershell
# Check Prometheus targets
Start-Process "http://localhost:9090/targets"

# Verify collector is exporting
curl http://localhost:8889/metrics

# Check Prometheus config
docker exec starterkit-prometheus cat /etc/prometheus/prometheus.yml
```

## 📚 Resources

- [OpenTelemetry Docs](https://opentelemetry.io/docs/)
- [Prometheus Docs](https://prometheus.io/docs/)
- [Grafana Docs](https://grafana.com/docs/)
- [Tempo Docs](https://grafana.com/docs/tempo/)
- [Loki Docs](https://grafana.com/docs/loki/)
- [Jaeger Docs](https://www.jaegertracing.io/docs/)

## 🎓 Best Practices

1. **Use OTEL Collector** as central hub
2. **Enable exemplars** for trace-to-metric correlation
3. **Use structured logging** with trace IDs
4. **Set appropriate retention policies**
5. **Monitor the monitors** (collector metrics)
6. **Use consistent service naming**
7. **Tag resources properly**
8. **Enable sampling** in production

## 🔐 Security Considerations

1. **Change default Grafana password** in production
2. **Use authentication** for Prometheus/Grafana
3. **Enable TLS** for OTLP endpoints
4. **Restrict network access** to observability services
5. **Implement RBAC** in Grafana
6. **Sanitize logs** to remove sensitive data

## 📈 Performance Tips

1. **Use batch processors** in OTEL Collector
2. **Enable compression** for OTLP exports
3. **Set memory limits** on collector
4. **Use tail-based sampling** for traces
5. **Configure appropriate scrape intervals**
6. **Use remote write** for Prometheus scalability

---

**Created**: 2026-01-20  
**Version**: 1.0  
**Maintained by**: StarterKit DevOps Team
