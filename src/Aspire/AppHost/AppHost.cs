using AppHost;

// Local development orchestration (dotnet run in src/Aspire/AppHost). Production runs docker compose
// (docker-compose.yml + deployment/), not this AppHost. Switches: AppHostSettings / wiki/Infrastructure/Aspire.md.
var builder = DistributedApplication.CreateBuilder(args);
var settings = AppHostSettings.From(builder.Configuration);

// ============================================================
// 1. Infrastructure (persistent: survives AppHost restarts, data in named volumes)
// ============================================================

// Host port is random unless Postgres:HostPort is set (e.g. for a desktop SQL client); a fixed 5432 would clash
// with a locally installed PostgreSQL. The password is generated once and kept in the AppHost user secrets.
var postgres = builder.AddPostgres("postgres", port: settings.PostgresHostPort)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithPgAdmin(pgAdmin => pgAdmin.WithLifetime(ContainerLifetime.Session));

// Connection string name "starterkit" = ConnectionStrings:starterkit used by every module DbContext.
var database = postgres.AddDatabase("starterkit");

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    // Redis Insight (official GUI, version pinned by Aspire) - Aspire's Redis Commander integration uses ':latest'.
    .WithRedisInsight(insight => insight.WithLifetime(ContainerLifetime.Session));

var seq = builder.AddSeq("seq")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// ============================================================
// 2. Application
// ============================================================
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    // Seq is optional for the API (log export retries in the background), so it does not gate startup.
    .WithReference(seq)
    .WithEnvironment("Messaging__Transport", settings.Transport.ToString())
    // Readiness = process + Postgres + Redis (Shared.Core HealthCheck); shown as the resource health.
    .WithHttpHealthCheck("/health");

if (settings.Transport == MessagingTransport.RabbitMq)
{
    var rabbitmq = builder.AddRabbitMQ("rabbitmq")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume()
        .WithManagementPlugin();

    api.WithReference(rabbitmq).WaitFor(rabbitmq);
}

// ============================================================
// 3. Observability
// ============================================================
// Always: Aspire injects OTEL_EXPORTER_OTLP_* (dashboard) into the API, which exports logs, traces and metrics there.
// Optional: the Grafana stack as a second destination (see ObservabilityStack).
if (settings.ObservabilityFullStack)
{
    var collector = builder.AddObservabilityStack();

    api.WithEnvironment("ObservabilityOptions__UseOTLPExporter", "true")
        .WithEnvironment(
            "ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint",
            collector.GetEndpoint(ObservabilityStack.CollectorOtlpGrpcEndpoint));
}

builder.Build().Run();
