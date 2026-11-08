using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Testcontainers.PostgreSql;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;
using MsOptions = Microsoft.Extensions.Options.Options;
using IMtOptions = Microsoft.Extensions.Options.IOptions<Shared.Core.MultiTenancy.MultiTenancyOptions>;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>
/// Hardened RLS bypass (<see cref="RlsBypassMode.Role"/>) on real Postgres: the runtime role is a plain non-superuser
/// that is NOT a member of <see cref="TenantRlsMigrationExtensions.BypassRoleName"/>, so even raw
/// <c>set_config('app.bypass_rls','on')</c> cannot lift RLS; host scope connects as a <c>BYPASSRLS</c> role.
/// </summary>
public sealed class RlsBypassRoleTests : IAsyncLifetime
{
    private const string Password = "pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _runtime = string.Empty;
    private string _host = string.Empty;
    private string _member = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();

        var statements = new List<string>
        {
            $"CREATE ROLE {TenantRlsMigrationExtensions.BypassRoleName} NOLOGIN",
            $"CREATE ROLE rt_app LOGIN PASSWORD '{Password}'",
            $"CREATE ROLE rt_host LOGIN PASSWORD '{Password}' BYPASSRLS",
            $"CREATE ROLE rt_member LOGIN PASSWORD '{Password}' IN ROLE {TenantRlsMigrationExtensions.BypassRoleName}",
            "CREATE SCHEMA app",
            "CREATE TABLE app.notes (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, text text NOT NULL)",
            "ALTER TABLE app.notes OWNER TO rt_app",
            "GRANT USAGE ON SCHEMA app TO rt_app, rt_host, rt_member",
            "GRANT ALL ON app.notes TO rt_host, rt_member"
        };
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.EnableTenantRowLevelSecurity("app", "notes");
        statements.AddRange(migration.Operations.OfType<SqlOperation>().Select(o => o.Sql));
        statements.Add($"INSERT INTO app.notes VALUES ('{Guid.NewGuid()}', '{TenantA.Id}', 'a1'), " +
                       $"('{Guid.NewGuid()}', '{TenantA.Id}', 'a2'), ('{Guid.NewGuid()}', '{TenantB.Id}', 'b1')");
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            await command.ExecuteNonQueryAsync();
        }

        string As(string user) => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        { Username = user, Password = Password }.ConnectionString;
        _runtime = As("rt_app");
        _host = As("rt_host");
        _member = As("rt_member");
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    [Fact]
    public async Task Runtime_role_cannot_switch_rls_off_with_the_session_variable()
    {
        var count = await RawCountAsync(_runtime, $"SELECT set_config('app.tenant_id', '{TenantA.Id}', false), " +
                                                  "set_config('app.bypass_rls', 'on', false)");
        count.Should().Be(2, "only the tenant's own rows; the variable is ignored for non-members of the bypass role");
    }

    [Fact]
    public async Task Runtime_role_without_tenant_sees_nothing_even_when_it_sets_the_variable()
    {
        (await RawCountAsync(_runtime, "SELECT set_config('app.bypass_rls', 'on', false)")).Should().Be(0);
    }

    [Fact]
    public async Task Member_of_the_bypass_role_can_still_use_the_variable()
    {
        (await RawCountAsync(_member, "SELECT set_config('app.bypass_rls', 'on', false)")).Should().Be(3);
    }

    [Fact]
    public async Task Host_scope_in_role_mode_connects_as_the_bypass_role_and_sees_everything()
    {
        using (TenantScope.BeginHostScope())
        {
            var (count, user) = await QueryAsync(Context(RoleOptions()));
            count.Should().Be(3);
            user.Should().Be("rt_host");
        }
    }

    [Fact]
    public async Task Tenant_scope_in_role_mode_stays_on_the_runtime_role_and_is_isolated()
    {
        using (TenantScope.Begin(TenantA))
        {
            var (count, user) = await QueryAsync(Context(RoleOptions()));
            count.Should().Be(2);
            user.Should().Be("rt_app");
        }
    }

    [Fact]
    public async Task Role_mode_restores_runtime_credentials_when_the_same_context_leaves_host_scope()
    {
        await using var db = Context(RoleOptions());
        const string countSql = "SELECT count(*)::int AS \"Value\" FROM app.notes";
        const string userSql = "SELECT current_user::text AS \"Value\"";

        using (TenantScope.BeginHostScope())
        {
            (await db.Database.SqlQueryRaw<string>(userSql).SingleAsync()).Should().Be("rt_host");
        }

        using (TenantScope.Begin(TenantA))
        {
            (await db.Database.SqlQueryRaw<string>(userSql).SingleAsync()).Should().Be("rt_app");
            (await db.Database.SqlQueryRaw<int>(countSql).SingleAsync()).Should().Be(2);
        }
    }

    [Fact]
    public async Task Role_mode_never_asks_the_connection_to_bypass_through_the_variable()
    {
        using (TenantScope.BeginHostScope())
        {
            await using var db = Context(RoleOptions());
            (await db.Database.SqlQueryRaw<string>("SELECT current_setting('app.bypass_rls') AS \"Value\"").SingleAsync())
                .Should().Be("off");
        }
    }

    [Fact]
    public void Role_mode_options_require_a_host_connection_string()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMultiTenancy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MultiTenancy:Database:RlsBypass"] = "Role"
        }).Build());

        var act = () => services.BuildServiceProvider().GetRequiredService<IMtOptions>().Value;

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>()
            .WithMessage("*HostConnectionString*");
    }

    [Fact]
    public async Task Startup_check_passes_when_the_bypass_group_role_exists()
    {
        var check = new RlsBypassRoleCheck(RoleOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RlsBypassRoleCheck>.Instance);

        await check.Invoking(c => c.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Startup_check_refuses_role_mode_when_the_bypass_group_role_is_missing()
    {
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP ROLE {TenantRlsMigrationExtensions.BypassRoleName}", admin);
            await drop.ExecuteNonQueryAsync();
        }

        var check = new RlsBypassRoleCheck(RoleOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RlsBypassRoleCheck>.Instance);

        await check.Invoking(c => c.StartAsync(CancellationToken.None)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage($"*{TenantRlsMigrationExtensions.BypassRoleName}*");
        (await RawCountAsync(_runtime, "SELECT set_config('app.bypass_rls', 'on', false)")).Should().Be(3,
            "this is exactly the unsafe state the check refuses to start in");
    }

    [Theory]
    [InlineData("rt_host", "bypassrls")]
    [InlineData("rt_member", "member of")]
    public async Task Startup_check_refuses_a_runtime_role_that_can_bypass_rls(string runtimeUser, string reason)
    {
        var check = new RlsBypassRoleCheck(RoleOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RlsBypassRoleCheck>.Instance,
            RuntimeConfig(runtimeUser == "rt_host" ? _host : _member));

        await check.Invoking(c => c.StartAsync(CancellationToken.None)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage($"*{runtimeUser}*{reason}*");
    }

    [Fact]
    public async Task Startup_check_refuses_a_superuser_runtime_role()
    {
        var check = new RlsBypassRoleCheck(RoleOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RlsBypassRoleCheck>.Instance,
            RuntimeConfig(_container.GetConnectionString()));

        await check.Invoking(c => c.StartAsync(CancellationToken.None)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*superuser=True*");
    }

    [Fact]
    public async Task Startup_check_accepts_a_plain_runtime_role()
    {
        var check = new RlsBypassRoleCheck(RoleOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RlsBypassRoleCheck>.Instance,
            RuntimeConfig(_runtime));

        await check.Invoking(c => c.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    private static IConfiguration RuntimeConfig(string connectionString) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:starterkit"] = connectionString
        }).Build();

    [Theory]
    [InlineData("Host=pgbouncer;Database=x;Username=u", true)]
    [InlineData("Host=db.internal;Port=6432;Database=x;Username=u", true)]
    [InlineData("Host=db.internal;Port=5432;Database=x;Username=u", false)]
    [InlineData("not a connection string ===", false)]
    [InlineData(null, false)]
    public void Pooling_heuristic_flags_pgbouncer_endpoints(string? connectionString, bool expected) =>
        PoolingSafetyCheck.LooksLikeTransactionPooler(connectionString).Should().Be(expected);

    private IMtOptions RoleOptions() => MsOptions.Create(new MultiTenancyOptions
    {
        Enabled = true,
        Database = new TenantDatabaseOptions { RlsBypass = RlsBypassMode.Role, HostConnectionString = _host }
    });

    private static async Task<int> RawCountAsync(string connectionString, string prepareSql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var prepare = new NpgsqlCommand(prepareSql, connection))
            await prepare.ExecuteNonQueryAsync();
        await using var count = new NpgsqlCommand("SELECT count(*)::int FROM app.notes", connection);
        return (int)(await count.ExecuteScalarAsync())!;
    }

    private static async Task<(int Count, string User)> QueryAsync(DbContext db)
    {
        await using (db)
        {
            var count = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM app.notes").SingleAsync();
            var user = await db.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync();
            return (count, user);
        }
    }

    private DbContext Context(IMtOptions options) =>
        new(new DbContextOptionsBuilder()
            .UseNpgsql(_runtime)
            .AddInterceptors(new TenantSessionConnectionInterceptor(
                new TenantContext(MsOptions.Create(new MultiTenancyOptions { Enabled = true })), options))
            .Options);
}
