using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Search.Data;
using Npgsql;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Search.IntegrationTests;

/// <summary>
/// The module as it runs in production: the REAL migration applied as the database owner, then the REAL module
/// (<c>AddMultiTenancy</c> + <c>AddSearchModule</c>) connecting as a NON-superuser role, so row-level security
/// applies under the handlers exactly like in a deployment.
/// </summary>
public sealed class SearchPostgresFixture : IAsyncLifetime
{
    private const string AppRole = "search_app";
    private const string AppPassword = "search_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider? _provider;

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002"), "globex", "Globex");

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // 1. The real migration, as the owner of the schema.
        var adminOptions = new DbContextOptionsBuilder<SearchDbContext>()
            .UseNpgsql(AdminConnectionString, b => b.UseModuleMigrationsHistory<SearchDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var admin = new SearchDbContext(adminOptions))
        {
            await admin.Database.MigrateAsync();
        }

        // 2. The application role: DML only, not a superuser and not the table owner, so RLS applies. (The schema exists
        //    once the module has a migration; the CREATE keeps a module without a model yet working.)
        await AdminExecAsync(
            "CREATE SCHEMA IF NOT EXISTS search; " +
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS; " +
            $"GRANT USAGE ON SCHEMA search TO {AppRole}; " +
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA search TO {AppRole};");
        var appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        // 3. The real module, as the app role.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DatabaseDefaults.ConnectionStringName}"] = appConnection,
                ["MultiTenancy:Enabled"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddMultiTenancy(configuration);
        services.AddSingleton<ITenantStore>(new FixedTenantStore(TenantA, TenantB));
        services.AddSingleton(new Mock<IHttpContextService> { DefaultValue = DefaultValue.Empty }.Object);

        services.AddSearchModule(configuration);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>One request of a member of <paramref name="tenant"/>: tenant scope + fresh DI scope, like the middleware.</summary>
    public async Task<T> SendAsync<T>(TenantInfo tenant, IRequest<T> request)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        }
    }

    /// <summary>SQL as the database owner (bypasses RLS): verification across tenants.</summary>
    public async Task<T?> AdminScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task AdminExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class FixedTenantStore(params TenantInfo[] tenants) : ITenantStore
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
public sealed class SearchPostgresCollection : ICollectionFixture<SearchPostgresFixture>
{
    public const string Name = "search-postgres";
}
