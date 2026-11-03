using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Testcontainers.PostgreSql;
using MsOptions = Microsoft.Extensions.Options.Options;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>
/// The DATABASE layer of tenant isolation (real Postgres): the policy installed by
/// <see cref="TenantRlsMigrationExtensions"/> + the session variables set by
/// <see cref="TenantSessionConnectionInterceptor"/>, exercised as a NON-superuser role (superusers bypass RLS).
/// Even raw SQL - which skips every EF filter - cannot cross tenants.
/// </summary>
public sealed class RowLevelSecurityTests : IAsyncLifetime
{
    private const string AppRole = "app_user";
    private const string AppPassword = "app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _appConnection = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();

        var statements = new List<string>
        {
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}'",
            "CREATE SCHEMA app",
            "CREATE TABLE app.notes (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, text text NOT NULL)",
            // The app role OWNS the table (as when it runs migrations): FORCE makes RLS apply to owners too.
            $"ALTER TABLE app.notes OWNER TO {AppRole}",
            $"GRANT USAGE ON SCHEMA app TO {AppRole}"
        };

        // The exact SQL the migration helper emits.
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.EnableTenantRowLevelSecurity("app", "notes");
        statements.AddRange(migration.Operations.OfType<SqlOperation>().Select(o => o.Sql));

        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = AppRole,
            Password = AppPassword
        };
        _appConnection = builder.ConnectionString;

        // Seed as host scope (bypass) through the interceptor, like a migration/seeder would.
        using (TenantScope.BeginHostScope())
        {
            await ExecuteAsync(MultiTenantContext(),
                $"INSERT INTO app.notes VALUES ('{Guid.NewGuid()}', '{TenantA.Id}', 'a1'), " +
                $"('{Guid.NewGuid()}', '{TenantA.Id}', 'a2'), ('{Guid.NewGuid()}', '{TenantB.Id}', 'b1')");
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Raw_sql_only_sees_the_current_tenants_rows()
    {
        using (TenantScope.Begin(TenantA))
        {
            (await CountAsync(MultiTenantContext())).Should().Be(2);
        }

        using (TenantScope.Begin(TenantB))
        {
            (await CountAsync(MultiTenantContext())).Should().Be(1);
        }
    }

    [Fact]
    public async Task No_tenant_sees_nothing()
    {
        using (TenantScope.BeginEmpty())
        {
            (await CountAsync(MultiTenantContext())).Should().Be(0);
        }
    }

    [Fact]
    public async Task Host_scope_sees_everything()
    {
        using (TenantScope.BeginHostScope())
        {
            (await CountAsync(MultiTenantContext())).Should().Be(3);
        }
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_violates_the_policy()
    {
        using (TenantScope.Begin(TenantA))
        {
            var act = () => ExecuteAsync(MultiTenantContext(),
                $"INSERT INTO app.notes VALUES ('{Guid.NewGuid()}', '{TenantB.Id}', 'forged')");
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }
    }

    [Fact]
    public async Task Pooled_connections_never_leak_the_previous_tenant()
    {
        // Same pool, alternating tenants: every logical open re-sets the session variables.
        for (var i = 0; i < 5; i++)
        {
            using (TenantScope.Begin(i % 2 == 0 ? TenantA : TenantB))
            {
                (await CountAsync(MultiTenantContext())).Should().Be(i % 2 == 0 ? 2 : 1);
            }
        }
    }

    [Fact]
    public async Task Disabled_rls_option_bypasses_the_policy()
    {
        var context = new TenantContext(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
        var options = MsOptions.Create(new MultiTenancyOptions
        {
            Enabled = true,
            Database = new TenantDatabaseOptions { EnableRowLevelSecurity = false }
        });

        using (TenantScope.Begin(TenantA))
        {
            await using var db = Context(context, options);
            (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM app.notes").SingleAsync())
                .Should().Be(3);
        }
    }

    private async Task<int> CountAsync(ITenantContext context)
    {
        await using var db = Context(context);
        return await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM app.notes").SingleAsync();
    }

    private async Task ExecuteAsync(ITenantContext context, string sql)
    {
        await using var db = Context(context);
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private DbContext Context(ITenantContext context, Microsoft.Extensions.Options.IOptions<MultiTenancyOptions>? options = null) =>
        new(new DbContextOptionsBuilder()
            .UseNpgsql(_appConnection)
            .AddInterceptors(new TenantSessionConnectionInterceptor(context,
                options ?? MsOptions.Create(new MultiTenancyOptions { Enabled = true })))
            .Options);
}
