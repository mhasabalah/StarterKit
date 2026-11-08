using System.Collections.Concurrent;
using System.Text.Json;
using MassTransit;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts.Messaging;
using Shared.Core.Data;
using Shared.Core.DDD;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Messaging;

// ----- Test integration events + aggregates ------------------------------------------------------------------------

[MessageUrn("shared-tests.thing-happened.v1")]
public sealed class ThingHappenedIntegrationEvent : IntegrationEvent
{
    public Guid ThingId { get; init; }
    public string Note { get; init; } = default!;
}

/// <summary>Deliberately has NO [MessageUrn]: the outbox must refuse it.</summary>
public sealed class UnpinnedIntegrationEvent : IntegrationEvent;

public sealed class HostThing : Aggregate<Guid>
{
    private HostThing() { }

    public string Name { get; private set; } = default!;

    public static HostThing Create(string? name = null) =>
        new() { Id = Guid.NewGuid(), Name = name ?? $"host-{Guid.NewGuid():N}" };

    public HostThing Raise(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
        return this;
    }

    public HostThing RaiseThingHappened(string note = "hello") =>
        Raise(new ThingHappenedIntegrationEvent { ThingId = Id, Note = note });
}

public sealed class TenantThing : Aggregate<Guid>, IMustHaveTenant
{
    private TenantThing() { }

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = default!;

    public static TenantThing Create() => new() { Id = Guid.NewGuid(), Name = $"tenant-{Guid.NewGuid():N}" };

    public TenantThing RaiseThingHappened(string note = "hello")
    {
        AddIntegrationEvent(new ThingHappenedIntegrationEvent { ThingId = Id, Note = note });
        return this;
    }
}

// ----- Contexts -----------------------------------------------------------------------------------------------------

/// <summary>Host context (no tenant data) WITH an outbox.</summary>
public sealed class OutboxHostDbContext(DbContextOptions<OutboxHostDbContext> options) : AppDbContextBase(options)
{
    public DbSet<HostThing> Things => Set<HostThing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("outbox_host_tests");
        builder.Entity<HostThing>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(t => t.Name).IsUnique(); // lets a test make a save fail on demand
        });
        builder.AddOutbox();
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}

/// <summary>Tenant-data context WITH a tenant outbox.</summary>
public sealed class OutboxTenantDbContext(DbContextOptions<OutboxTenantDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<TenantThing> Things => Set<TenantThing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("outbox_tenant_tests");
        builder.Entity<TenantThing>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        });
        builder.AddTenantOutbox();
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
        ApplyTenantFilters(builder);
    }
}

/// <summary>A context that saves aggregates raising integration events but maps NO outbox (a wiring mistake).</summary>
public sealed class NoOutboxDbContext(DbContextOptions<NoOutboxDbContext> options) : AppDbContextBase(options)
{
    public DbSet<HostThing> Things => Set<HostThing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("outbox_none_tests");
        builder.Entity<HostThing>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        });
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}

// ----- Test doubles -------------------------------------------------------------------------------------------------

public sealed class TestClock : TimeProvider
{
    public static readonly DateTime Start = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private DateTime _now = Start;

    public DateTime UtcNow => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Reset() => _now = Start;

    public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);
}

public sealed record PublishedMessage(object Message, Type Type, Guid? MessageId, Guid? TenantId, bool IsHostScope);

/// <summary>Fake bus: records every publish together with the ambient tenant at publish time; can be told to fail.</summary>
public sealed class RecordingPublisher
{
    private readonly ConcurrentQueue<PublishedMessage> _published = new();

    public RecordingPublisher(Func<ITenantContext> tenantContext)
    {
        Endpoint = new Mock<IPublishEndpoint>();
        Endpoint.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Returns<object, Type, IPipe<PublishContext>, CancellationToken>(async (message, type, pipe, _) =>
            {
                var context = new Mock<PublishContext>();
                context.SetupAllProperties();
                await pipe.Send(context.Object);
                await Task.Yield(); // let concurrent relays interleave

                var failure = Failure?.Invoke(message);
                if (failure is not null)
                    throw failure;

                var tenant = tenantContext();
                _published.Enqueue(new PublishedMessage(message, type, context.Object.MessageId, tenant.TenantId,
                    tenant.IsHostScope));
            });
    }

    public Mock<IPublishEndpoint> Endpoint { get; }

    /// <summary>Return an exception to make that publish fail (nothing is recorded).</summary>
    public Func<object, Exception?>? Failure { get; set; }

    public IReadOnlyList<PublishedMessage> Published => _published.ToList();

    public void Reset()
    {
        Failure = null;
        _published.Clear();
    }
}

// ----- Fixture ------------------------------------------------------------------------------------------------------

/// <summary>
/// ONE real PostgreSQL + the real Shared.Core wiring (<c>AddCustomDbContext</c>: all interceptors, collector, unit of
/// work runner, outbox services) for a host context with an outbox, a tenant-data context with a tenant outbox and a
/// context without any outbox. Multi-tenancy is ON; the clock and the bus are fakes. Tests of the collection run
/// sequentially and call <see cref="ResetAsync"/> first, so they never see each other's rows.
/// </summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1"), "outbox-a", "Outbox A");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2"), "outbox-b", "Outbox B");
    public static readonly Guid GoneTenantId = Guid.Parse("cccccccc-0000-0000-0000-0000000000c3");

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public TestClock Clock { get; } = new();
    public OutboxOptions Options { get; } = new();
    public RecordingPublisher Bus { get; private set; } = default!;
    public IServiceProvider Services => _provider;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString()
            })
            .Build();

        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserId()).Returns("outbox-test-user");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(http.Object);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ITenantStore>(new TestTenantStore(TenantA, TenantB));
        services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
        services.AddSingleton<IOptions<OutboxOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
        Shared.Core.Extensions.MediatorExtensions.AddMediatorHandlers(services);
        services.AddSingleton(sp =>
        {
            Bus = new RecordingPublisher(() => sp.GetRequiredService<ITenantContext>());
            return Bus;
        });
        services.AddSingleton(sp => sp.GetRequiredService<RecordingPublisher>().Endpoint.Object);

        services.AddCustomDbContext<OutboxHostDbContext>(configuration, "starterkit");
        services.AddCustomDbContext<OutboxTenantDbContext>(configuration, "starterkit");
        services.AddCustomDbContext<NoOutboxDbContext>(configuration, "starterkit");
        services.AddOutbox<OutboxHostDbContext>();
        services.AddOutbox<OutboxTenantDbContext>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Bus = _provider.GetRequiredService<RecordingPublisher>();

        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxHostDbContext>().Database.EnsureCreatedAsync();
            // EnsureCreated is a no-op once any table exists: create the other contexts' tables explicitly.
            await CreateTablesAsync(scope.ServiceProvider.GetRequiredService<OutboxTenantDbContext>());
            await CreateTablesAsync(scope.ServiceProvider.GetRequiredService<NoOutboxDbContext>());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static Task CreateTablesAsync(DbContext context) =>
        ((IRelationalDatabaseCreator)context.GetService<IDatabaseCreator>()).CreateTablesAsync();

    /// <summary>Clean slate for a test: no rows anywhere, defaults restored, clock at its start.</summary>
    public async Task ResetAsync()
    {
        Clock.Reset();
        Bus.Reset();
        Options.BatchSize = 100;
        Options.MaxAttempts = 10;
        Options.LeaseSeconds = 60;
        Options.RetentionDays = 7;

        using (TenantScope.BeginHostScope())
        {
            await using var scope = _provider.CreateAsyncScope();
            var host = scope.ServiceProvider.GetRequiredService<OutboxHostDbContext>();
            await host.Set<OutboxMessage>().ExecuteDeleteAsync();
            await host.Things.ExecuteDeleteAsync();
            var tenant = scope.ServiceProvider.GetRequiredService<OutboxTenantDbContext>();
            await tenant.Set<TenantOutboxMessage>().IgnoreQueryFilters().ExecuteDeleteAsync();
            await tenant.Things.IgnoreQueryFilters().ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<NoOutboxDbContext>().Things.ExecuteDeleteAsync();
        }
    }

    /// <summary>Runs <paramref name="work"/> in a fresh DI scope (= one request / unit of work).</summary>
    public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScope(Func<IServiceProvider, Task> work) => InScope(async sp =>
    {
        await work(sp);
        return true;
    });

    // ----- seeding / reading ---------------------------------------------------------------------------------------

    /// <summary>A valid, due, pending row of the host outbox.</summary>
    public OutboxMessage NewHostRow(Action<OutboxMessage>? tweak = null, Guid? tenantId = null)
    {
        var evt = NewEvent();
        var row = new OutboxMessage { TenantId = tenantId };
        Fill(row, evt);
        tweak?.Invoke(row);
        return row;
    }

    /// <summary>A valid, due, pending row of the tenant outbox.</summary>
    public TenantOutboxMessage NewTenantRow(Guid tenantId, Action<TenantOutboxMessage>? tweak = null)
    {
        var row = new TenantOutboxMessage { TenantId = tenantId };
        Fill(row, NewEvent());
        tweak?.Invoke(row);
        return row;
    }

    public ThingHappenedIntegrationEvent NewEvent(string note = "seeded") => new()
    {
        EventId = Guid.NewGuid(),
        OccurredOn = Clock.UtcNow,
        ThingId = Guid.NewGuid(),
        Note = note
    };

    private void Fill(OutboxMessageBase row, ThingHappenedIntegrationEvent evt)
    {
        row.Id = evt.EventId;
        row.MessageType = IntegrationEventTypes.NameOf(typeof(ThingHappenedIntegrationEvent));
        row.Payload = JsonSerializer.Serialize(evt, Json);
        row.OccurredOn = evt.OccurredOn;
        row.NextAttemptAt = Clock.UtcNow;
    }

    public async Task SeedAsync(params OutboxMessage[] rows)
    {
        using (TenantScope.BeginHostScope())
        {
            await InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxHostDbContext>();
                db.AddRange(rows);
                await db.SaveChangesAsync();
            });
        }
    }

    public async Task SeedAsync(params TenantOutboxMessage[] rows)
    {
        using (TenantScope.BeginHostScope())
        {
            await InScope(async sp =>
            {
                var db = sp.GetRequiredService<OutboxTenantDbContext>();
                db.AddRange(rows);
                await db.SaveChangesAsync();
            });
        }
    }

    public async Task<OutboxMessage> HostRow(Guid id)
    {
        using (TenantScope.BeginHostScope())
        {
            return await InScope(sp => sp.GetRequiredService<OutboxHostDbContext>().Set<OutboxMessage>()
                .AsNoTracking().SingleAsync(m => m.Id == id));
        }
    }

    public async Task<TenantOutboxMessage> TenantRow(Guid id)
    {
        using (TenantScope.BeginHostScope())
        {
            return await InScope(sp => sp.GetRequiredService<OutboxTenantDbContext>().Set<TenantOutboxMessage>()
                .AsNoTracking().SingleAsync(m => m.Id == id));
        }
    }

    public async Task<List<OutboxMessage>> HostRows()
    {
        using (TenantScope.BeginHostScope())
        {
            return await InScope(sp => sp.GetRequiredService<OutboxHostDbContext>().Set<OutboxMessage>()
                .AsNoTracking().OrderBy(m => m.OccurredOn).ToListAsync());
        }
    }

    public async Task<List<TenantOutboxMessage>> TenantRows()
    {
        using (TenantScope.BeginHostScope())
        {
            return await InScope(sp => sp.GetRequiredService<OutboxTenantDbContext>().Set<TenantOutboxMessage>()
                .AsNoTracking().OrderBy(m => m.OccurredOn).ToListAsync());
        }
    }

    /// <summary>Delivers the host outbox with a relay of its own scope.</summary>
    public async Task<int> DeliverHostAsync(IReadOnlyCollection<Guid>? ids = null)
    {
        using (TenantScope.BeginHostScope())
        {
            return await InScope(sp => sp.GetRequiredService<OutboxRelay>()
                .DeliverAsync(sp.GetRequiredService<OutboxHostDbContext>(), ids));
        }
    }
}

public sealed class TestTenantStore(params TenantInfo[] tenants) : ITenantStore
{
    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Id == tenantId));

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Identifier == identifier));

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantInfo?>(null);

    public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantInfo>>(tenants);
}

[CollectionDefinition(Name)]
public sealed class OutboxCollection : ICollectionFixture<OutboxFixture>
{
    public const string Name = "outbox-postgres";
}
