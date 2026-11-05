using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Auditing;
using Shared.Core.Data;
using Shared.Core.DDD;
using Shared.Core.MultiTenancy;
using Shared.Core.Tests.Messaging;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Auditing;

// ----- Test entities + contexts -------------------------------------------------------------------------------------

public sealed class AuditedNote : Aggregate<Guid>, IAuditable
{
    private AuditedNote() { }

    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public int Count { get; set; }
    public NoteState State { get; set; }
    public string? PasswordHint { get; set; }
    public string? ApiToken { get; set; }
    public string? SecretNote { get; set; }
    public string? ContentHash { get; set; }
    [AuditIgnore] public string? Scratch { get; set; }
    public NoteOwnedValue Owned { get; set; } = new("a", "b");

    public static AuditedNote Create(string? title = null) =>
        new() { Id = Guid.NewGuid(), Title = title ?? $"note-{Guid.NewGuid():N}" };
}

public enum NoteState { Draft, Done }

public sealed record NoteOwnedValue(string En, string Ar);

public sealed class AuditedTenantNote : Aggregate<Guid>, IAuditable, IMustHaveTenant
{
    private AuditedTenantNote() { }

    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;

    public static AuditedTenantNote Create() => new() { Id = Guid.NewGuid(), Title = "t" };
}

/// <summary>Not audited: proves the interceptor ignores non-IAuditable entities.</summary>
public sealed class PlainNote : Aggregate<Guid>
{
    public string Title { get; set; } = "plain";
}

public sealed class AuditHostDbContext(DbContextOptions<AuditHostDbContext> options) : AppDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("audit_host_tests");
        builder.Entity<AuditedNote>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
            b.Property(t => t.Title).HasMaxLength(5000).IsRequired();
            b.HasIndex(t => t.Title).IsUnique(); // lets a test make a save fail on demand
            b.OwnsOne(t => t.Owned);
        });
        builder.Entity<PlainNote>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
        });
        builder.AddAuditTrail();
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}

public sealed class AuditTenantDbContext(DbContextOptions<AuditTenantDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("audit_tenant_tests");
        builder.Entity<AuditedTenantNote>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
        });
        builder.AddTenantAuditTrail();
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
        ApplyTenantFilters(builder);
    }
}

/// <summary>Saves IAuditable entities but maps no audit table (a wiring mistake the interceptor must refuse).</summary>
public sealed class AuditMissingDbContext(DbContextOptions<AuditMissingDbContext> options) : AppDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("audit_missing_tests");
        builder.Entity<AuditedNote>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.DomainEvents);
            b.Ignore(t => t.IntegrationEvents);
            b.OwnsOne(t => t.Owned);
        });
        builder.ToSnakeCaseTables();
    }
}

// ----- Fixture ------------------------------------------------------------------------------------------------------

public sealed class AuditFixture : IAsyncLifetime
{
    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1"), "audit-a", "Audit A");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2"), "audit-b", "Audit B");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public TestClock Clock { get; } = new();
    public Guid? User { get; set; }
    public string? Impersonator { get; set; }
    public string? Correlation { get; set; }
    public string? Ip { get; set; }
    public AuditTrailOptions Options { get; } = new();

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
        http.Setup(h => h.GetUserId()).Returns(() => User?.ToString());
        http.Setup(h => h.GetUserIdAsGuid()).Returns(() => User);
        http.Setup(h => h.GetClaimValue(TenantClaimTypes.Actor)).Returns(() => Impersonator);
        http.Setup(h => h.GetCorrelationId()).Returns(() => Correlation);
        http.Setup(h => h.GetIpAddress()).Returns(() => Ip);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(http.Object);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ITenantStore>(new TestTenantStore(TenantA, TenantB));
        services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
        Shared.Core.Extensions.MediatorExtensions.AddMediatorHandlers(services);
        services.AddCustomDbContext<AuditHostDbContext>(configuration, "starterkit");
        services.AddCustomDbContext<AuditTenantDbContext>(configuration, "starterkit");
        services.AddCustomDbContext<AuditMissingDbContext>(configuration, "starterkit");
        // The one mutable options instance (registered last, so it wins over the configuration-bound default).
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<AuditTrailOptions>>(
            Microsoft.Extensions.Options.Options.Create(Options));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditHostDbContext>().Database.EnsureCreatedAsync();
            await CreateTablesAsync(scope.ServiceProvider.GetRequiredService<AuditTenantDbContext>());
            await CreateTablesAsync(scope.ServiceProvider.GetRequiredService<AuditMissingDbContext>());
        }
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static Task CreateTablesAsync(DbContext context) =>
        ((IRelationalDatabaseCreator)context.GetService<IDatabaseCreator>()).CreateTablesAsync();

    public async Task ResetAsync()
    {
        Clock.Reset();
        User = null;
        Impersonator = null;
        Correlation = null;
        Ip = null;
        Options.MaxValueLength = 512;
        Options.MaxChangesBytes = 16 * 1024;

        using (TenantScope.BeginHostScope())
        {
            await using var scope = _provider.CreateAsyncScope();
            var host = scope.ServiceProvider.GetRequiredService<AuditHostDbContext>();
            await host.Set<AuditEntry>().ExecuteDeleteAsync();
            await host.Set<AuditedNote>().ExecuteDeleteAsync();
            await host.Set<PlainNote>().ExecuteDeleteAsync();
            var tenant = scope.ServiceProvider.GetRequiredService<AuditTenantDbContext>();
            await tenant.Set<TenantAuditEntry>().IgnoreQueryFilters().ExecuteDeleteAsync();
            await tenant.Set<AuditedTenantNote>().IgnoreQueryFilters().ExecuteDeleteAsync();
        }
    }

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

    public Task<List<AuditEntry>> HostEntries() => InScope(sp =>
        sp.GetRequiredService<AuditHostDbContext>().Set<AuditEntry>().AsNoTracking().OrderBy(a => a.OccurredAt).ToListAsync());
}

[CollectionDefinition(Name)]
public sealed class AuditCollection : ICollectionFixture<AuditFixture>
{
    public const string Name = "audit-trail";
}

// ----- Tests --------------------------------------------------------------------------------------------------------

[Collection(AuditCollection.Name)]
public sealed class AuditTrailTests(AuditFixture fx) : IAsyncLifetime
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    public Task InitializeAsync() => fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> InsertAsync(Action<AuditedNote>? tweak = null)
    {
        var note = AuditedNote.Create();
        tweak?.Invoke(note);
        await fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            db.Add(note);
            await db.SaveChangesAsync();
        });
        return note.Id;
    }

    private Task UpdateAsync(Guid id, Action<AuditedNote> change) => fx.InScope(async sp =>
    {
        var db = sp.GetRequiredService<AuditHostDbContext>();
        var note = await db.Set<AuditedNote>().SingleAsync(n => n.Id == id);
        change(note);
        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Insert_RecordsAnAddedEntry_WithNewValues_AndNoBookkeeping()
    {
        fx.User = Guid.NewGuid();
        var id = await InsertAsync(n => { n.Count = 3; n.State = NoteState.Done; });

        var entry = (await fx.HostEntries()).Should().ContainSingle().Subject;
        entry.EntityType.Should().Be(nameof(AuditedNote));
        entry.EntityId.Should().Be(id.ToString());
        entry.Action.Should().Be(AuditAction.Added);
        entry.ActorUserId.Should().Be(fx.User);
        var changes = Parse(entry.ChangesJson);
        changes.GetProperty("Count").GetProperty("new").GetInt32().Should().Be(3);
        changes.GetProperty("State").GetProperty("new").GetString().Should().Be("Done");
        changes.GetProperty("Owned.En").GetProperty("new").GetString().Should().Be("a");
        changes.GetProperty("Count").TryGetProperty("old", out _).Should().BeFalse();
        foreach (var bookkeeping in new[] { "Version", "CreatedAt", "CreatedBy", "LastModified", "LastModifiedBy", "Id" })
            changes.TryGetProperty(bookkeeping, out _).Should().BeFalse($"{bookkeeping} is bookkeeping");
    }

    [Fact]
    public async Task Update_RecordsOnlyTheChangedProperties_AsOldAndNew_IncludingOwnedValues()
    {
        var id = await InsertAsync(n => n.Body = "before");

        await UpdateAsync(id, n =>
        {
            n.Body = "after";
            n.Owned = new NoteOwnedValue("a", "changed");
        });

        var entries = await fx.HostEntries();
        entries.Should().HaveCount(2);
        var update = entries.Last();
        update.Action.Should().Be(AuditAction.Modified);
        var changes = Parse(update.ChangesJson);
        changes.GetProperty("Body").GetProperty("old").GetString().Should().Be("before");
        changes.GetProperty("Body").GetProperty("new").GetString().Should().Be("after");
        changes.GetProperty("Owned.Ar").GetProperty("new").GetString().Should().Be("changed");
        changes.TryGetProperty("Title", out _).Should().BeFalse("unchanged properties are not recorded");
        changes.TryGetProperty("Owned.En", out _).Should().BeFalse("unchanged owned values are not recorded");
        changes.TryGetProperty("Version", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Update_ChangingOnlyAnIgnoredProperty_RecordsNothing()
    {
        var id = await InsertAsync();

        await UpdateAsync(id, n => n.Scratch = "noise");

        (await fx.HostEntries()).Should().ContainSingle("only the insert");
    }

    [Fact]
    public async Task Delete_RecordsADeletedEntry_WithTheOldValues()
    {
        var id = await InsertAsync(n => n.Body = "bye");

        await fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            db.Remove(await db.Set<AuditedNote>().SingleAsync(n => n.Id == id));
            await db.SaveChangesAsync();
        });

        var deleted = (await fx.HostEntries()).Last();
        deleted.Action.Should().Be(AuditAction.Deleted);
        var changes = Parse(deleted.ChangesJson);
        changes.GetProperty("Body").GetProperty("old").GetString().Should().Be("bye");
        changes.GetProperty("Body").TryGetProperty("new", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SecretsAndIgnoredProperties_AreNeverRecorded_OnInsertOrUpdate()
    {
        var id = await InsertAsync(n =>
        {
            n.PasswordHint = "hunter2";
            n.ApiToken = "tok-123";
            n.SecretNote = "s3cret";
            n.ContentHash = "abc";
            n.Scratch = "scratch";
        });
        await UpdateAsync(id, n =>
        {
            n.PasswordHint = "hunter3";
            n.ApiToken = "tok-456";
            n.Body = "visible";
        });

        var entries = await fx.HostEntries();
        foreach (var entry in entries)
        {
            foreach (var secret in new[] { "hunter", "tok-", "s3cret", "abc", "scratch", "Password", "ApiToken", "SecretNote", "ContentHash", "Scratch" })
                entry.ChangesJson.Should().NotContain(secret);
        }

        Parse(entries.Last().ChangesJson).GetProperty("Body").GetProperty("new").GetString().Should().Be("visible");
    }

    [Fact]
    public async Task Entry_CarriesActor_Impersonator_Correlation_Ip_AndTime()
    {
        fx.User = Guid.NewGuid();
        var staff = Guid.NewGuid();
        fx.Impersonator = staff.ToString();
        fx.Correlation = "corr-1";
        fx.Ip = "10.1.2.3";

        await InsertAsync();

        var entry = (await fx.HostEntries()).Single();
        entry.ActorUserId.Should().Be(fx.User);
        entry.ImpersonatorUserId.Should().Be(staff);
        entry.CorrelationId.Should().Be("corr-1");
        entry.IpAddress.Should().Be("10.1.2.3");
        entry.OccurredAt.Should().Be(TestClock.Start);
    }


    [Fact]
    public async Task NonAuditableEntities_AreNotAudited()
    {
        await fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            db.Add(new PlainNote { Id = Guid.NewGuid() });
            await db.SaveChangesAsync();
        });

        (await fx.HostEntries()).Should().BeEmpty();
    }

    [Fact]
    public async Task AuditRow_AndEntity_ShareOneTransaction_AFailedSaveLeavesNeitherBehind()
    {
        var title = $"dup-{Guid.NewGuid():N}";
        await InsertAsync(n => n.Title = title);
        var before = (await fx.HostEntries()).Count;

        var act = () => fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            db.Add(AuditedNote.Create());                 // would be audited
            db.Add(AuditedNote.Create(title));            // unique violation fails the whole SaveChanges
            await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateException>();
        (await fx.HostEntries()).Should().HaveCount(before, "the audit rows rolled back with the failed save");
        (await fx.InScope(sp => sp.GetRequiredService<AuditHostDbContext>().Set<AuditedNote>().CountAsync()))
            .Should().Be(1);
    }

    [Fact]
    public async Task LongValues_AreCut_AndAnOversizedDiff_IsReducedToPropertyNames()
    {
        fx.Options.MaxValueLength = 10;
        var id = await InsertAsync(n => n.Body = new string('x', 100));
        var inserted = Parse((await fx.HostEntries()).Single().ChangesJson);
        inserted.GetProperty("Body").GetProperty("new").GetString().Should().HaveLength(13).And.EndWith("...");

        // A diff above the byte cap keeps only the names. (Options are resolved per scope: PostConfigure reads the fixture.)
        fx.Options.MaxChangesBytes = 60;
        await UpdateAsync(id, n => { n.Body = "short"; n.Count = 9; n.State = NoteState.Done; });

        var capped = Parse((await fx.HostEntries()).Last().ChangesJson);
        capped.GetProperty("_truncated").GetBoolean().Should().BeTrue();
        capped.GetProperty("properties").EnumerateArray().Select(p => p.GetString()).Should().Contain("Body");
    }

    [Fact]
    public async Task ModifyingOrDeletingAnAuditRow_ThroughTheChangeTracker_IsRejected()
    {
        await InsertAsync();

        var modify = () => fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            var row = await db.Set<AuditEntry>().FirstAsync();
            row.EntityType = "Tampered";
            await db.SaveChangesAsync();
        });
        var delete = () => fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditHostDbContext>();
            db.Remove(await db.Set<AuditEntry>().FirstAsync());
            await db.SaveChangesAsync();
        });

        await modify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        (await fx.HostEntries()).Should().ContainSingle().Which.EntityType.Should().Be(nameof(AuditedNote));
    }

    [Fact]
    public async Task RetentionPurge_RemovesOnlyOldEntries_InBatches()
    {
        await InsertAsync();
        fx.Clock.Advance(TimeSpan.FromDays(10));
        await InsertAsync();
        await InsertAsync();

        var removed = await fx.InScope(sp => sp.GetRequiredService<AuditHostDbContext>()
            .PurgeAuditEntriesAsync(TestClock.Start.AddDays(5), batchSize: 100));

        removed.Should().Be(1);
        (await fx.HostEntries()).Should().HaveCount(2);
    }

    [Fact]
    public async Task AuditableEntities_InAContextWithoutAnAuditTable_AreRefused()
    {
        var act = () => fx.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AuditMissingDbContext>();
            db.Add(AuditedNote.Create());
            await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*maps no audit table*");
    }

    [Fact]
    public async Task TenantContext_StampsTheTenant_AndEachTenantSeesOnlyItsOwnEntries()
    {
        using (TenantScope.Begin(AuditFixture.TenantA))
        {
            await fx.InScope(async sp =>
            {
                var db = sp.GetRequiredService<AuditTenantDbContext>();
                db.Add(AuditedTenantNote.Create());
                await db.SaveChangesAsync();
            });
        }

        using (TenantScope.Begin(AuditFixture.TenantB))
        {
            var seenByB = await fx.InScope(sp =>
                sp.GetRequiredService<AuditTenantDbContext>().Set<TenantAuditEntry>().AsNoTracking().ToListAsync());
            seenByB.Should().BeEmpty("tenant B must not see tenant A's audit trail");
        }

        using (TenantScope.Begin(AuditFixture.TenantA))
        {
            var seenByA = await fx.InScope(sp =>
                sp.GetRequiredService<AuditTenantDbContext>().Set<TenantAuditEntry>().AsNoTracking().ToListAsync());
            seenByA.Should().ContainSingle().Which.TenantId.Should().Be(AuditFixture.TenantA.Id);
        }
    }
}
