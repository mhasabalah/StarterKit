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
// The pgvector image of the PostgreSQL major Aspire uses (18): the Agentic module's knowledge search needs the extension,
// and keeping the major version lets existing data volumes start.
var postgres = builder.AddPostgres("postgres", port: settings.PostgresHostPort)
    .WithImage("pgvector/pgvector", "0.8.7-pg18")
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

// Multi-tenancy with a second database for a dedicated tenant, on the same Postgres server. Aspire creates the database
// and hands the API ConnectionStrings:tenant-demo; a tenant created with isolation Dedicated and
// connectionSecretRef "tenant-demo" resolves to it, and its first provisioning migrates it.
if (settings.DedicatedTenantSample)
{
    var tenantDatabase = postgres.AddDatabase(AppHostSettings.DedicatedTenantConnectionName, "starterkit_tenant_demo");

    api.WithReference(tenantDatabase).WaitFor(tenantDatabase)
        .WithEnvironment("MultiTenancy__Enabled", "true");
}

// Upload malware scanning (same image as docker-compose.clamav.yml). The first start loads signatures for 1-2 minutes;
// the API does not wait for it - uploads get 503 malware_scanner_unavailable until clamd answers.
if (settings.ClamAv)
{
    var clamav = builder.AddContainer("clamav", "clamav/clamav", "1.4")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithVolume("starterkit-clamav-db", "/var/lib/clamav")
        .WithEnvironment("CLAMD_CONF_AlertExceedsMax", "yes") // skipped content is never "OK" (docker-compose.clamav.yml)
        .WithEndpoint(targetPort: 3310, name: "clamd", scheme: "tcp");

    var clamd = clamav.GetEndpoint("clamd");
    api.WithEnvironment("Attachment__Validation__EnableVirusScanning", "true")
        .WithEnvironment("Attachment__Validation__Scanner__Provider", "ClamAv")
        .WithEnvironment("Attachment__Validation__Scanner__Host", clamd.Property(EndpointProperty.Host))
        .WithEnvironment("Attachment__Validation__Scanner__Port", clamd.Property(EndpointProperty.Port));
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
