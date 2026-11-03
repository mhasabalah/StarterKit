using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Testcontainers.PostgreSql;
using MsOptions = Microsoft.Extensions.Options.Options;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>
/// Real Postgres: the generic tenant data copy used by tenant moves (<see cref="TenantDataMover"/>) and the
/// data export (<see cref="TenantDataExporter"/>) - only the tenant's rows travel, retries are idempotent, schema
/// drift and "moving onto yourself" are refused.
/// </summary>
public sealed class TenantDataMoveTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _source = string.Empty;
    private string _target = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _source = WithDatabase("source_db");
        _target = WithDatabase("target_db");

        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        foreach (var db in new[] { "source_db", "target_db" })
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE {db}", admin);
            await create.ExecuteNonQueryAsync();
        }

        using (TenantScope.BeginHostScope())
        {
            foreach (var connection in new[] { _source, _target })
            {
                await using var db = Context(connection);
                await db.Database.EnsureCreatedAsync();
            }

            await using var source = Context(_source);
            source.Notes.AddRange(
                new Note { Id = Guid.NewGuid(), TenantId = TenantA.Id, Text = "a1" },
                new Note { Id = Guid.NewGuid(), TenantId = TenantA.Id, Text = "a2", BranchId = Guid.NewGuid() },
                new Note { Id = Guid.NewGuid(), TenantId = TenantB.Id, Text = "b1" });
            await source.SaveChangesAsync();

            // The target already hosts another tenant, plus a leftover of a failed earlier attempt for tenant A.
            await using var target = Context(_target);
            target.Notes.AddRange(
                new Note { Id = Guid.NewGuid(), TenantId = TenantB.Id, Text = "other tenant" },
                new Note { Id = Guid.NewGuid(), TenantId = TenantA.Id, Text = "stale leftover" });
            await target.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Copies_only_the_tenants_rows_and_replaces_leftovers()
    {
        IReadOnlyDictionary<string, long> copied;
        using (TenantScope.BeginHostScope())
        {
            await using var source = Context(_source);
            await using var target = Context(_target);
            copied = await TenantDataMover.CopyAsync(source, target, TenantA.Id);
        }

        copied.Values.Single().Should().Be(2);

        using (TenantScope.BeginHostScope())
        {
            await using var target = Context(_target);
            var rows = await target.Notes.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
                .OrderBy(n => n.Text).Select(n => new { n.TenantId, n.Text }).ToListAsync();
            rows.Should().BeEquivalentTo([
                new { TenantId = TenantA.Id, Text = "a1" },
                new { TenantId = TenantA.Id, Text = "a2" },
                new { TenantId = TenantB.Id, Text = "other tenant" }
            ]);
        }
    }

    [Fact]
    public async Task Copy_is_idempotent()
    {
        for (var i = 0; i < 2; i++)
        {
            using (TenantScope.BeginHostScope())
            {
                await using var source = Context(_source);
                await using var target = Context(_target);
                (await TenantDataMover.CopyAsync(source, target, TenantA.Id)).Values.Single().Should().Be(2);
            }
        }
    }

    [Fact]
    public async Task Schema_drift_is_refused_and_nothing_is_written()
    {
        await using (var admin = new NpgsqlConnection(_target))
        {
            await admin.OpenAsync();
            await using var alter = new NpgsqlCommand("ALTER TABLE notes ADD COLUMN extra text", admin);
            await alter.ExecuteNonQueryAsync();
        }

        using (TenantScope.BeginHostScope())
        {
            await using var source = Context(_source);
            await using var target = Context(_target);
            var act = () => TenantDataMover.CopyAsync(source, target, TenantA.Id);
            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Schema");

            await using var check = Context(_target);
            (await check.Notes.IgnoreQueryFilters([QueryFilterNames.Tenant]).CountAsync(n => n.Text == "stale leftover"))
                .Should().Be(1, "the target transaction was rolled back");
        }
    }

    [Fact]
    public async Task Moving_onto_the_same_database_is_refused()
    {
        TenantDataMover.SameDatabase(_source, _source + ";Pooling=false").Should().BeTrue();
        TenantDataMover.SameDatabase(_source, _target).Should().BeFalse();

        using (TenantScope.BeginHostScope())
        {
            await using var a = Context(_source);
            await using var b = Context(_source);
            var act = () => TenantDataMover.CopyAsync(a, b, TenantA.Id);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Export_writes_the_tenants_rows_as_csv()
    {
        using var buffer = new MemoryStream();
        using (TenantScope.Begin(TenantA))
        {
            await using var context = Context(_source);
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true);
            (await TenantDataExporter.WriteCsvAsync(context, TenantA.Id, zip, "data")).Should().Be(1);
        }

        buffer.Position = 0;
        using var read = new ZipArchive(buffer, ZipArchiveMode.Read);
        var entry = read.Entries.Single();
        entry.FullName.Should().Be("data/public.notes.csv");

        using var reader = new StreamReader(entry.Open());
        var lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3, "header + tenant A's two rows");
        lines[0].Should().Contain("tenant_id");
        lines.Skip(1).Should().OnlyContain(l => l.Contains(TenantA.Id.ToString()));
    }

    private string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private static NotesDbContext Context(string connection)
    {
        var tenantContext = MultiTenantContext();
        var services = new ServiceCollection().AddSingleton(tenantContext).BuildServiceProvider();
        return new NotesDbContext(new DbContextOptionsBuilder<NotesDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .UseApplicationServiceProvider(services)
            .AddInterceptors(
                new TenantEntityInterceptor(tenantContext, NullLogger<TenantEntityInterceptor>.Instance),
                new TenantSessionConnectionInterceptor(tenantContext,
                    MsOptions.Create(new MultiTenancyOptions { Enabled = true })))
            .Options);
    }
}
