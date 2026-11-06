using Microsoft.Extensions.Logging.Abstractions;
using AppHost;

namespace AppHost.Tests;

/// <summary>
/// Inspects the AppHost resource graph WITHOUT starting it (no Docker needed). Guards the defects found when the
/// orchestration was first run live: colliding host ports, floating image tags, an always-on 13-container stack,
/// and telemetry not wired to the API.
/// </summary>
public class AppHostModelTests
{
    private static async Task<IDistributedApplicationTestingBuilder> Model(params string[] args) =>
        await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(args);

    private static IResource Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Should().ContainSingle(r => r.Name == name).Subject;

    private static async Task<Dictionary<string, string>> ApiEnvironment(IDistributedApplicationTestingBuilder builder)
    {
        var api = (IResourceWithEnvironment)Resource(builder, "api");
        var configuration = await ExecutionConfigurationBuilder.Create(api)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, CancellationToken.None);
        return configuration.EnvironmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static readonly string[] StackResources = ["otel-collector", "tempo", "loki", "jaeger", "prometheus", "grafana"];

    [Fact]
    public async Task Default_model_runs_only_what_the_api_needs()
    {
        await using var builder = await Model();

        var names = builder.Resources.Select(r => r.Name).ToList();
        names.Should().Contain(["postgres", "starterkit", "redis", "seq", "api"]);
        names.Should().NotContain(StackResources, "the Grafana stack is opt-in; the Aspire dashboard covers everyday telemetry");
        names.Should().NotContain("rabbitmq");
    }

    [Fact]
    public async Task Api_references_its_dependencies_by_connection_string_name()
    {
        await using var builder = await Model();

        var env = await ApiEnvironment(builder);

        // Names the API reads: ConnectionStrings:starterkit (modules), :redis (RedisConnectionConfiguration), :seq.
        env.Should().ContainKeys("ConnectionStrings__starterkit", "ConnectionStrings__redis", "ConnectionStrings__seq");
        env.Should().Contain("Messaging__Transport", "InMemory");
        env.Should().NotContainKey("ObservabilityOptions__UseOTLPExporter");
    }

    [Fact]
    public async Task Api_waits_for_database_and_redis_but_not_for_seq()
    {
        await using var builder = await Model();

        var waits = Resource(builder, "api").Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).ToList();

        waits.Should().Contain(["starterkit", "redis"]);
        waits.Should().NotContain("seq", "log shipping is optional and must not gate API startup");
    }

    [Fact]
    public async Task Api_reports_readiness_through_its_health_endpoint()
    {
        await using var builder = await Model();

        Resource(builder, "api").Annotations.OfType<HealthCheckAnnotation>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Full_stack_is_added_on_demand_and_wired_into_the_api()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true");

        builder.Resources.Select(r => r.Name).Should().Contain(StackResources);

        var env = await ApiEnvironment(builder);
        env.Should().Contain("ObservabilityOptions__UseOTLPExporter", "true");
        env.Should().ContainKey("ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint")
            .WhoseValue.Should().Contain(ObservabilityStack.CollectorName, "the endpoint comes from the collector resource, not a hard-coded port");
    }

    [Fact]
    public async Task No_two_resources_claim_the_same_fixed_host_port()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true", $"{AppHostSettings.TransportKey}=RabbitMq");

        var fixedPorts = builder.Resources
            .SelectMany(r => r.Annotations.OfType<EndpointAnnotation>().Select(e => (Resource: r.Name, e.Port)))
            .Where(e => e.Port is not null)
            .ToList();

        fixedPorts.GroupBy(e => e.Port).Where(g => g.Count() > 1)
            .Should().BeEmpty("fixed host ports collide (otel-collector and tempo both took 4317/4318 before)");
    }

    [Fact]
    public async Task Custom_containers_have_no_fixed_host_ports()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true");

        foreach (var name in StackResources)
        {
            Resource(builder, name).Annotations.OfType<EndpointAnnotation>()
                .Should().OnlyContain(e => e.Port == null, $"{name} must let Aspire allocate its host port");
        }
    }

    [Fact]
    public async Task Every_container_image_is_pinned()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true", $"{AppHostSettings.TransportKey}=RabbitMq");

        var images = builder.Resources.OfType<ContainerResource>()
            .Select(r => (r.Name, Image: r.Annotations.OfType<ContainerImageAnnotation>().Single()))
            .ToList();

        images.Should().NotBeEmpty();
        images.Should().OnlyContain(
            i => !string.IsNullOrEmpty(i.Image.Tag) && i.Image.Tag != "latest",
            "a floating tag changes the dev environment without a code change");
    }

    [Fact]
    public async Task Every_stack_container_has_a_health_check()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true");

        foreach (var name in StackResources)
        {
            Resource(builder, name).Annotations.OfType<HealthCheckAnnotation>()
                .Should().NotBeEmpty($"{name} should report readiness, not just 'Running'");
        }
    }

    [Fact]
    public async Task Bind_mount_sources_exist()
    {
        await using var builder = await Model($"{AppHostSettings.ObservabilityFullStackKey}=true");

        var mounts = builder.Resources
            .SelectMany(r => r.Annotations.OfType<ContainerMountAnnotation>())
            .Where(m => m.Type == ContainerMountType.BindMount)
            .ToList();

        mounts.Should().NotBeEmpty();
        mounts.Should().OnlyContain(m => File.Exists(m.Source) || Directory.Exists(m.Source));
        mounts.Should().OnlyContain(m => m.IsReadOnly, "config mounts must not be modified by the containers");
    }

    [Fact]
    public async Task RabbitMq_transport_adds_the_broker_and_wires_the_api()
    {
        await using var builder = await Model($"{AppHostSettings.TransportKey}=RabbitMq");

        Resource(builder, "rabbitmq");
        var env = await ApiEnvironment(builder);
        env.Should().Contain("Messaging__Transport", "RabbitMq");
        env.Should().ContainKey("ConnectionStrings__rabbitmq");
    }

    [Fact]
    public async Task ClamAv_switch_adds_clamd_and_turns_scanning_on_in_the_api()
    {
        await using (var lean = await Model())
        {
            lean.Resources.Select(r => r.Name).Should().NotContain("clamav");
        }

        await using var builder = await Model($"{AppHostSettings.ClamAvKey}=true");

        Resource(builder, "clamav");
        var env = await ApiEnvironment(builder);
        env.Should().Contain("Attachment__Validation__EnableVirusScanning", "true");
        env.Should().Contain("Attachment__Validation__Scanner__Provider", "ClamAv");
        env.Should().ContainKey("Attachment__Validation__Scanner__Host");
        env.Should().ContainKey("Attachment__Validation__Scanner__Port");
    }

    [Fact]
    public async Task Postgres_host_port_is_random_unless_configured()
    {
        await using (var random = await Model())
        {
            Resource(random, "postgres").Annotations.OfType<EndpointAnnotation>()
                .Should().OnlyContain(e => e.Port == null, "a fixed 5432 clashes with a locally installed PostgreSQL");
        }

        await using var fixedPort = await Model($"{AppHostSettings.PostgresHostPortKey}=55432");
        Resource(fixedPort, "postgres").Annotations.OfType<EndpointAnnotation>()
            .Should().Contain(e => e.Port == 55432);
    }
}
