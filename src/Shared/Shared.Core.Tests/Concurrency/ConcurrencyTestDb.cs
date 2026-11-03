using Microsoft.EntityFrameworkCore;
using Moq;
using Shared.Core.Data;
using Shared.Core.Data.Interceptors;
using Shared.Core.DDD;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Concurrency;

/// <summary>Aggregate used only by the concurrency tests. It has an owned value object so we can test owned-only changes.</summary>
public sealed class Widget : Aggregate<Guid>
{
    private Widget() { }

    public string Name { get; private set; } = default!;
    public Dimensions Size { get; private set; } = default!;

    public static Widget Create(string name, decimal width, decimal height) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Size = new Dimensions(width, height)
    };

    public void Rename(string name) => Name = name;

    public void Resize(decimal width, decimal height) => Size = new Dimensions(width, height);
}

public sealed record Dimensions(decimal Width, decimal Height);

public sealed class ConcurrencyTestDbContext(DbContextOptions<ConcurrencyTestDbContext> options)
    : AppDbContextBase(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("concurrency_tests");

        builder.Entity<Widget>(b =>
        {
            b.HasKey(w => w.Id);
            b.Ignore(w => w.DomainEvents);
            b.Property(w => w.Name).HasMaxLength(100).IsRequired();
            b.OwnsOne(w => w.Size);
        });

        // Same conventions as every module DbContext.
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}

/// <summary>
/// One real PostgreSQL container for the whole test collection. Each test uses fresh Widget ids,
/// so the tests stay isolated and can share the schema.
/// </summary>
public sealed class PostgresConcurrencyFixture : IAsyncLifetime
{
    public const string CurrentUserId = "test-user";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A new context models a new HTTP request: its own change tracker and the real interceptor.</summary>
    public ConcurrencyTestDbContext CreateContext()
    {
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserId()).Returns(CurrentUserId);

        // Mirror Shared.Core AddCustomDbContext exactly (naming convention + interceptor; UTC timestamptz).
        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableEntityInterceptor(http.Object))
            .Options;

        return new ConcurrencyTestDbContext(options);
    }

    public async Task<Widget> SeedAsync(string name = "widget", decimal width = 1, decimal height = 1)
    {
        await using var db = CreateContext();
        var widget = Widget.Create(name, width, height);
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget;
    }

    public async Task<Widget> ReloadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.Widgets.AsNoTracking().SingleAsync(w => w.Id == id);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresConcurrencyCollection : ICollectionFixture<PostgresConcurrencyFixture>
{
    public const string Name = "postgres-concurrency";
}
