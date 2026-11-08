using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Core.MultiTenancy;
using Shared.Messaging.Extensions;
using Testcontainers.PostgreSql;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.Messaging;

/// <summary>A message only these tests publish; consumed by <see cref="TenantProbeConsumer"/>.</summary>
public sealed record TenantProbe(Guid Id);

/// <summary>Records the ambient tenant the bus restored for each probe.</summary>
public sealed class TenantProbeConsumer(ITenantContext tenantContext) : IConsumer<TenantProbe>
{
    public static readonly ConcurrentDictionary<Guid, TaskCompletionSource<(Guid? TenantId, bool IsHost)>> Seen = new();

    public Task Consume(ConsumeContext<TenantProbe> context)
    {
        Seen.GetOrAdd(context.Message.Id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult((tenantContext.TenantId, tenantContext.IsHostScope));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The REAL bus configuration (<c>AddMassTransitWithAssemblies</c>: retry policies + tenant publish/consume filters) on
/// the in-memory transport, end to end: publish → header → consume filter → consumer. This is the security-relevant
/// path of messaging (a consumer running as the wrong tenant writes into the wrong tenant), and it is transport- and
/// library-agnostic: the same assertions must hold after any change of transport or message bus. Runs on every
/// durable-or-default transport we ship (<see cref="InMemoryTenantMessagingBusTests"/>,
/// <see cref="PostgreSqlTenantMessagingBusTests"/>).
/// </summary>
public abstract class TenantMessagingBusTests : IAsyncLifetime
{
    private readonly MutableTenantStore _tenants = new(TenantA, TenantB);
    private ServiceProvider _provider = null!;
    private IBusControl _bus = null!;
    private List<IHostedService> _hosted = [];

    /// <summary>Transport settings (<c>Messaging:*</c>, connection strings).</summary>
    protected abstract Task<Dictionary<string, string?>> TransportAsync();

    protected virtual Task CleanupAsync() => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantStore>(_tenants);
        services.AddSingleton(MultiTenantContext());
        services.AddMassTransitWithAssemblies(
            new ConfigurationBuilder().AddInMemoryCollection(await TransportAsync()).Build(),
            typeof(TenantMessagingBusTests).Assembly);

        _provider = services.BuildServiceProvider();
        _bus = _provider.GetRequiredService<IBusControl>();

        // As the host does: transport migration (PostgreSql) first, then the bus.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _hosted = _provider.GetServices<IHostedService>().ToList();
        foreach (var service in _hosted)
            await service.StartAsync(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = _hosted.Count - 1; i >= 0; i--)
            await _hosted[i].StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
        await CleanupAsync();
    }

    private async Task<Guid> PublishAsync(Func<IDisposable?> scope)
    {
        var id = Guid.NewGuid();
        using (scope())
        {
            await _bus.Publish(new TenantProbe(id));
        }

        return id;
    }

    private static Task<(Guid? TenantId, bool IsHost)> Observed(Guid id) =>
        TenantProbeConsumer.Seen.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    [Fact]
    public async Task Consumer_runs_as_the_tenant_that_published()
    {
        var forA = await PublishAsync(() => TenantScope.Begin(TenantA));
        var forB = await PublishAsync(() => TenantScope.Begin(TenantB));

        (await Observed(forA).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be((TenantA.Id, false));
        (await Observed(forB).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be((TenantB.Id, false));
    }

    [Fact]
    public async Task Host_scope_travels_as_host_scope()
    {
        var id = await PublishAsync(() => TenantScope.BeginHostScope());

        (await Observed(id).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(((Guid?)null, true));
    }

    [Fact]
    public async Task A_tenantless_message_is_consumed_without_a_tenant()
    {
        var id = await PublishAsync(() => null);

        (await Observed(id).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(((Guid?)null, false));
    }

    [Fact]
    public async Task A_message_of_a_purged_tenant_is_dropped_never_consumed_tenantless()
    {
        var purged = new TenantInfo(Guid.NewGuid(), "purged", "Purged");

        var id = await PublishAsync(() => TenantScope.Begin(purged));

        var consumed = await Task.WhenAny(Observed(id), Task.Delay(TimeSpan.FromSeconds(3))) == Observed(id);
        consumed.Should().BeFalse("the consume filter drops messages whose tenant no longer exists");
    }

    [Fact]
    public async Task A_message_of_a_migrating_tenant_waits_and_is_delivered_once_the_tenant_is_back()
    {
        _tenants.Set(TenantA with { Status = TenantStatus.Migrating });

        var id = await PublishAsync(() => TenantScope.Begin(TenantA));

        var early = await Task.WhenAny(Observed(id), Task.Delay(TimeSpan.FromSeconds(2))) == Observed(id);
        early.Should().BeFalse("writes would land in the database being left");

        _tenants.Set(TenantA);
        (await Observed(id).WaitAsync(TimeSpan.FromSeconds(30))).Should().Be((TenantA.Id, false),
            "the outer retry policy redelivers it after the migration");
    }

    [Fact]
    public async Task A_migrating_tenant_does_not_block_other_tenants()
    {
        _tenants.Set(TenantA with { Status = TenantStatus.Migrating });
        using (TenantScope.Begin(TenantA))
        {
            // Far more than the endpoint's concurrency limit (2 x CPU cores by default).
            for (var i = 0; i < 200; i++)
                await _bus.Publish(new TenantProbe(Guid.NewGuid()));
        }

        var other = await PublishAsync(() => TenantScope.Begin(TenantB));

        (await Observed(other).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be((TenantB.Id, false),
            "deferred messages of a migrating tenant must not occupy the consumer slots of everyone else");
    }

    private sealed class MutableTenantStore(params TenantInfo[] initial) : ITenantStore
    {
        private readonly ConcurrentDictionary<Guid, TenantInfo> _tenants = new(initial.ToDictionary(t => t.Id));

        public void Set(TenantInfo tenant) => _tenants[tenant.Id] = tenant;

        public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.GetValueOrDefault(tenantId));

        public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.Values.FirstOrDefault(t => t.Identifier == identifier));

        public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants.Values.ToList());
    }
}

public sealed class InMemoryTenantMessagingBusTests : TenantMessagingBusTests
{
    protected override Task<Dictionary<string, string?>> TransportAsync() =>
        Task.FromResult(new Dictionary<string, string?> { ["Messaging:Transport"] = "InMemory" });
}

/// <summary>The production default transport (docker-compose: <c>MESSAGING_TRANSPORT=PostgreSql</c>) on real Postgres.</summary>
public sealed class PostgreSqlTenantMessagingBusTests : TenantMessagingBusTests
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected override async Task<Dictionary<string, string?>> TransportAsync()
    {
        await _db.StartAsync();
        return new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "PostgreSql",
            ["ConnectionStrings:starterkit"] = _db.GetConnectionString()
        };
    }

    protected override Task CleanupAsync() => _db.DisposeAsync().AsTask();
}
