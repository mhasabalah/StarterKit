using System.Collections.Concurrent;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Shared.Core.Data;
using Shared.Core.Messaging.Sagas;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Shared.Core.Web;
using Shared.Messaging.Extensions;
using Testcontainers.PostgreSql;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.Messaging;

// ----- A test-only saga: a counter per correlation id, in a tenant-data context ------------------------------------

public sealed record CounterStarted(Guid CorrelationId);

public sealed record CounterIncremented(Guid CorrelationId, int By = 1);

public sealed record CounterFinished(Guid CorrelationId);

/// <summary>Arms the saga's timeout: a message scheduled through the bus's message scheduler.</summary>
public sealed record CounterArmed(Guid CorrelationId);

/// <summary>The scheduled timeout message, delivered back to the saga after <see cref="CounterStateMachine.TimeoutDelay"/>.</summary>
public sealed record CounterTimedOut(Guid CorrelationId);

/// <summary>Published by the saga when it finishes or jumps; observed by <see cref="CounterNoticeConsumer"/>.</summary>
public sealed record CounterNotice(Guid CorrelationId, int Count);

public sealed class CounterState : SagaStateMachineInstance, IMustHaveTenant
{
    public Guid CorrelationId { get; set; }
    public Guid TenantId { get; set; }
    public string CurrentState { get; set; } = default!;
    public int Count { get; set; }

    /// <summary>The token of the scheduled timeout (MassTransit unschedules/correlates with it).</summary>
    public Guid? TimeoutTokenId { get; set; }
}

public sealed class CounterStateMachine : MassTransitStateMachine<CounterState>
{
    public static readonly TimeSpan TimeoutDelay = TimeSpan.FromSeconds(2);

    public CounterStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Schedule(() => Timeout, x => x.TimeoutTokenId, s =>
        {
            s.Delay = TimeoutDelay;
            s.Received = r => r.CorrelateById(m => m.Message.CorrelationId);
        });
        Event(() => Armed, e => e.CorrelateById(m => m.Message.CorrelationId));

        Event(() => Started, e => e.CorrelateById(m => m.Message.CorrelationId));
        Event(() => Incremented, e =>
        {
            e.CorrelateById(m => m.Message.CorrelationId);
            // Another tenant's message never finds this instance (tenant filter): drop it, never fault.
            e.OnMissingInstance(m => m.Discard());
        });
        Event(() => Finished, e => e.CorrelateById(m => m.Message.CorrelationId));

        Initially(When(Started).TransitionTo(Running));

        During(Running,
            When(Incremented)
                .Then(c => c.Saga.Count += c.Message.By)
                .Publish(c => new CounterNotice(c.Saga.CorrelationId, c.Saga.Count)),
            When(Finished)
                .Publish(c => new CounterNotice(c.Saga.CorrelationId, c.Saga.Count))
                .TransitionTo(Done),
            When(Armed)
                .Schedule(Timeout, c => new CounterTimedOut(c.Saga.CorrelationId)),
            When(Timeout!.Received)
                .TransitionTo(TimedOut));
    }

    public State Running { get; private set; } = default!;
    public State Done { get; private set; } = default!;
    public State TimedOut { get; private set; } = default!;

    public Event<CounterArmed> Armed { get; private set; } = default!;
    public Schedule<CounterState, CounterTimedOut> Timeout { get; private set; } = default!;

    public Event<CounterStarted> Started { get; private set; } = default!;
    public Event<CounterIncremented> Incremented { get; private set; } = default!;
    public Event<CounterFinished> Finished { get; private set; } = default!;
}

public sealed class CounterStateConfiguration : SagaStateConfiguration<CounterState>
{
    protected override void ConfigureState(EntityTypeBuilder<CounterState> builder)
    {
        builder.Property(x => x.CurrentState).HasMaxLength(StateNameMaxLength).IsRequired();
        // Lets a test make the saga's save fail on demand (Count >= 1000).
        builder.ToTable(t => t.HasCheckConstraint("ck_counter_states_count", "count < 1000"));
    }
}

/// <summary>A tenant-data module context holding the saga state, wired by the real <c>AddCustomDbContext</c>.</summary>
public sealed class CounterSagaDbContext(DbContextOptions<CounterSagaDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<CounterState> Counters => Set<CounterState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("saga_tests");
        builder.ApplyConfiguration(new CounterStateConfiguration());
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
        ApplyTenantFilters(builder);
    }
}

public sealed class CounterNoticeConsumer : IConsumer<CounterNotice>
{
    public static readonly ConcurrentQueue<CounterNotice> Seen = new();

    public Task Consume(ConsumeContext<CounterNotice> context)
    {
        Seen.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

// ----- Tests ---------------------------------------------------------------------------------------------------------

/// <summary>
/// The durable saga registration (<see cref="DurableSagaExtensions.AddDurableSagaStateMachine{TMachine,TState,TDbContext}"/>,
/// ADR-0023) on the REAL bus configuration and a real PostgreSQL. State survives a restart, concurrent events lose no
/// update, the state is tenant-isolated, and the saga publishes nothing when its state cannot be saved. Runs on every
/// transport we ship (<see cref="InMemoryDurableSagaTests"/>, <see cref="PostgreSqlDurableSagaTests"/>).
/// </summary>
public abstract class DurableSagaTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = null!;
    private List<IHostedService> _hosted = [];

    /// <summary>Transport settings (<c>Messaging:*</c>) for the given saga database.</summary>
    protected abstract Dictionary<string, string?> Transport(string connectionString);

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await StartAsync();

        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CounterSagaDbContext>();
            await ((IRelationalDatabaseCreator)context.GetService<IDatabaseCreator>()).CreateTablesAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _db.DisposeAsync();
    }

    /// <summary>A fresh host (container + bus) on the same database: what a deploy or a crash does.</summary>
    private async Task StartAsync()
    {
        var settings = Transport(_db.GetConnectionString());
        settings["ConnectionStrings:starterkit"] = _db.GetConnectionString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserId()).Returns("saga-test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(http.Object);
        services.AddSingleton<ITenantStore>(new TestTenantStore(TenantA, TenantB));
        services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
        Shared.Core.Extensions.MediatorExtensions.AddMediatorHandlers(services);
        services.AddCustomDbContext<CounterSagaDbContext>(configuration, DatabaseDefaults.ConnectionStringName);
        services.AddMassTransitWithAssemblies(configuration,
            bus => bus.AddDurableSagaStateMachine<CounterStateMachine, CounterState, CounterSagaDbContext>(),
            typeof(DurableSagaTests).Assembly);

        _provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _hosted = _provider.GetServices<IHostedService>().ToList();
        foreach (var service in _hosted)
            await service.StartAsync(cts.Token);
    }

    private async Task StopAsync()
    {
        for (var i = _hosted.Count - 1; i >= 0; i--)
            await _hosted[i].StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    private async Task PublishAsync(TenantInfo tenant, object message)
    {
        using (TenantScope.Begin(tenant))
        {
            await _provider.GetRequiredService<IBus>().Publish(message);
        }
    }

    /// <summary>The persisted state, read past the tenant filter (null = no row).</summary>
    private async Task<CounterState?> ReadAsync(Guid id)
    {
        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            return await scope.ServiceProvider.GetRequiredService<CounterSagaDbContext>().Counters.AsNoTracking()
                .IgnoreNonIsolationFilters().SingleOrDefaultAsync(c => c.CorrelationId == id);
        }
    }

    private async Task<CounterState> WaitForAsync(Guid id, Func<CounterState, bool> condition)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            var state = await ReadAsync(id);
            if (state is not null && condition(state))
                return state;
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(100, CancellationToken.None);
        }
    }

    private static async Task<CounterNotice> NoticeAsync(Guid id, Func<CounterNotice, bool> condition)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            var notice = CounterNoticeConsumer.Seen.FirstOrDefault(n => n.CorrelationId == id && condition(n));
            if (notice is not null)
                return notice;
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(100, CancellationToken.None);
        }
    }

    [Fact]
    public async Task State_survives_a_bus_restart_and_the_saga_completes()
    {
        var id = Guid.NewGuid();
        await PublishAsync(TenantA, new CounterStarted(id));
        // Messages are consumed concurrently: an increment that overtakes the start finds no instance and is
        // discarded (OnMissingInstance). Wait until the instance exists, as every other test does.
        await WaitForAsync(id, s => s.CurrentState == "Running");
        await PublishAsync(TenantA, new CounterIncremented(id));
        await WaitForAsync(id, s => s.Count == 1);

        await StopAsync();
        await StartAsync();

        await PublishAsync(TenantA, new CounterIncremented(id));
        await WaitForAsync(id, s => s.Count == 2);
        await PublishAsync(TenantA, new CounterFinished(id));

        (await WaitForAsync(id, s => s.CurrentState == "Done")).Count.Should().Be(2,
            "the state written before the restart was loaded from the database, not lost with the old process");
    }

    [Fact]
    public async Task Concurrent_events_for_one_instance_lose_no_update()
    {
        var id = Guid.NewGuid();
        await PublishAsync(TenantA, new CounterStarted(id));
        await WaitForAsync(id, s => s.CurrentState == "Running");

        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => PublishAsync(TenantA, new CounterIncremented(id))));

        (await WaitForAsync(id, s => s.Count >= 25)).Count.Should().Be(25,
            "the pessimistic lock applies the events of one instance one after another");
    }

    [Fact]
    public async Task State_is_stamped_with_the_message_tenant_and_invisible_to_another_tenant()
    {
        var id = Guid.NewGuid();
        await PublishAsync(TenantA, new CounterStarted(id));
        var created = await WaitForAsync(id, s => s.CurrentState == "Running");
        created.TenantId.Should().Be(TenantA.Id, "the bus restores the message tenant and the interceptor stamps it");

        // Same correlation id, other tenant: the lookup is tenant-filtered, so B never reaches A's instance.
        await PublishAsync(TenantB, new CounterIncremented(id, By: 100));
        // A's own event afterwards proves the queue moved past B's message.
        await PublishAsync(TenantA, new CounterIncremented(id));

        (await WaitForAsync(id, s => s.Count >= 1)).Count.Should().Be(1, "tenant B's event must not change tenant A's state");
    }

    [Fact]
    public async Task Publish_from_a_saga_is_not_sent_when_its_state_save_fails()
    {
        var id = Guid.NewGuid();
        await PublishAsync(TenantA, new CounterStarted(id));
        await WaitForAsync(id, s => s.CurrentState == "Running");

        // Count 5000 violates the check constraint: every save of this step fails (and is retried).
        await PublishAsync(TenantA, new CounterIncremented(id, By: 5000));
        await Task.Delay(TimeSpan.FromSeconds(5));
        CounterNoticeConsumer.Seen.Should().NotContain(n => n.CorrelationId == id,
            "the in-memory outbox releases the saga's messages only after its state is committed");

        // Control: a step whose save succeeds publishes.
        await PublishAsync(TenantA, new CounterIncremented(id, By: 2));
        (await NoticeAsync(id, n => n.Count == 2)).Count.Should().Be(2);
        (await ReadAsync(id))!.Count.Should().Be(2, "the failed step never reached the database");
    }

    [Fact]
    public async Task A_scheduled_timeout_is_delivered_back_to_the_saga_in_its_tenant()
    {
        var id = Guid.NewGuid();
        await PublishAsync(TenantA, new CounterStarted(id));
        await WaitForAsync(id, s => s.CurrentState == "Running");

        await PublishAsync(TenantA, new CounterArmed(id));
        var armed = await WaitForAsync(id, s => s.TimeoutTokenId is not null);
        armed.CurrentState.Should().Be("Running", "the timeout is not due yet");

        (await WaitForAsync(id, s => s.CurrentState == "TimedOut")).TenantId.Should().Be(TenantA.Id,
            "the transport's message scheduler delivered the timeout, carrying the tenant header back to the saga");
    }
}

public sealed class InMemoryDurableSagaTests : DurableSagaTests
{
    protected override Dictionary<string, string?> Transport(string connectionString) =>
        new() { ["Messaging:Transport"] = "InMemory" };
}

/// <summary>The production default transport: bus and saga state in the same PostgreSQL.</summary>
public sealed class PostgreSqlDurableSagaTests : DurableSagaTests
{
    protected override Dictionary<string, string?> Transport(string connectionString) =>
        new() { ["Messaging:Transport"] = "PostgreSql" };
}
