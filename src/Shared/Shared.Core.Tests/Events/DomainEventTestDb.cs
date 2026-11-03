using System.Collections.Concurrent;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Data;
using Shared.Contracts.Events;
using Shared.Core.DDD;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Events;

// ----- Test aggregate + events ------------------------------------------------------------------------------------

public sealed record GadgetCreatedEvent(Guid GadgetId, string Name) : IDomainEvent;

public sealed record GadgetRenamedEvent(Guid GadgetId, string Name) : IDomainEvent;

public sealed class Gadget : Aggregate<Guid>
{
    /// <summary>Names with this prefix make <see cref="GadgetCreatedHandler"/> rename the gadget (raising a new event).</summary>
    public const string ChainPrefix = "chain:";

    private Gadget() { }

    public string Name { get; private set; } = default!;

    public static Gadget Create(string name)
    {
        var gadget = new Gadget { Id = Guid.NewGuid(), Name = name };
        gadget.AddDomainEvent(new GadgetCreatedEvent(gadget.Id, name));
        return gadget;
    }

    public void Rename(string name)
    {
        Name = name;
        AddDomainEvent(new GadgetRenamedEvent(Id, name));
    }
}

public sealed class DomainEventTestDbContext(DbContextOptions<DomainEventTestDbContext> options)
    : AppDbContextBase(options)
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("domain_event_tests");

        builder.Entity<Gadget>(b =>
        {
            b.HasKey(g => g.Id);
            b.Ignore(g => g.DomainEvents);
            b.Property(g => g.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(g => g.Name).IsUnique(); // lets tests make a save fail on demand
        });

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}

// ----- Published-event log + handlers -----------------------------------------------------------------------------

/// <summary>Every published domain event, in order. Shared by the collection; tests filter by their own gadget ids.</summary>
public sealed class PublishedEvents
{
    private readonly ConcurrentQueue<IDomainEvent> _events = new();

    public void Add(IDomainEvent domainEvent) => _events.Enqueue(domainEvent);

    public IReadOnlyList<IDomainEvent> For(Guid gadgetId) => _events
        .Where(e => e switch
        {
            GadgetCreatedEvent c => c.GadgetId == gadgetId,
            GadgetRenamedEvent r => r.GadgetId == gadgetId,
            _ => false
        })
        .ToList();
}

public sealed class GadgetCreatedHandler(PublishedEvents log, DomainEventTestDbContext db)
    : INotificationHandler<GadgetCreatedEvent>
{
    public async Task Handle(GadgetCreatedEvent notification, CancellationToken cancellationToken)
    {
        log.Add(notification);

        // A handler that changes state and raises a NEW event: it must be published in the same drain.
        if (notification.Name.StartsWith(Gadget.ChainPrefix, StringComparison.Ordinal))
        {
            var gadget = await db.Gadgets.SingleAsync(g => g.Id == notification.GadgetId, cancellationToken);
            gadget.Rename("renamed-" + notification.Name);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class GadgetRenamedHandler(PublishedEvents log) : INotificationHandler<GadgetRenamedEvent>
{
    public Task Handle(GadgetRenamedEvent notification, CancellationToken cancellationToken)
    {
        log.Add(notification);
        return Task.CompletedTask;
    }
}

// ----- Commands -----------------------------------------------------------------------------------------------------

/// <summary>Creates a gadget. With <see cref="InnerName"/>, sends a nested CreateGadgetCommand after its own save.</summary>
public sealed record CreateGadgetCommand(string Name, string? InnerName = null, bool FailAfterSave = false)
    : IRequest<CreateGadgetResult>;

/// <param name="PublishedDuringHandler">Events (for the created ids) already published when the handler returned.</param>
public sealed record CreateGadgetResult(Guid Id, Guid? InnerId, int PublishedDuringHandler);

public sealed class CreateGadgetHandler(DomainEventTestDbContext db, ISender sender, PublishedEvents log)
    : IRequestHandler<CreateGadgetCommand, CreateGadgetResult>
{
    public async Task<CreateGadgetResult> Handle(CreateGadgetCommand request, CancellationToken cancellationToken)
    {
        var gadget = Gadget.Create(request.Name);
        db.Gadgets.Add(gadget);
        await db.SaveChangesAsync(cancellationToken);

        Guid? innerId = null;
        if (request.InnerName is not null)
        {
            innerId = (await sender.Send(new CreateGadgetCommand(request.InnerName), cancellationToken)).Id;
        }

        if (request.FailAfterSave)
        {
            throw new InvalidOperationException("handler failed after a committed save");
        }

        var published = log.For(gadget.Id).Count + (innerId is { } id ? log.For(id).Count : 0);
        return new CreateGadgetResult(gadget.Id, innerId, published);
    }
}

// ----- Fixture ------------------------------------------------------------------------------------------------------

/// <summary>
/// Real PostgreSQL + the real Shared.Core wiring: <c>AddCustomDbContext</c> (interceptors, collector, dispatcher,
/// runner) and <c>AddMediatR</c> (validation, concurrency, logging and DispatchDomainEvents behaviors).
/// </summary>
public sealed class DomainEventFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public IServiceProvider Services => _provider;
    public PublishedEvents Published { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString()
            })
            .Build();

        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserId()).Returns("event-test-user");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(http.Object);
        services.AddSingleton(Published);
        Extensions.MediatRExtentions.AddMediatR(services, typeof(DomainEventFixture).Assembly);
        services.AddCustomDbContext<DomainEventTestDbContext>(configuration, "starterkit");

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DomainEventTestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static string UniqueName(string prefix = "gadget") => $"{prefix}-{Guid.NewGuid():N}";
}

[CollectionDefinition(Name)]
public sealed class DomainEventCollection : ICollectionFixture<DomainEventFixture>
{
    public const string Name = "domain-events-postgres";
}
