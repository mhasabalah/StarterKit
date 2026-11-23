using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Search.Contracts.Indexing;
using Search.Data;
using Search.Domains.Indexes.Models;
using Search.Engine;
using Search.IntegrationTests.Conformance;
using Search.IntegrationTests.TestIndex;
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
/// applies under the engine exactly like in a deployment. The test index <see cref="TestArticles"/> is registered.
/// </summary>
public sealed class SearchPostgresFixture : IAsyncLifetime, ISearchEngineTestHost
{
    private const string AppRole = "search_app";
    private const string AppPassword = "search_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider? _provider;
    private string _appConnection = "";

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002"), "globex", "Globex");

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public ArticleStore Articles { get; } = new();

    public RegisteredIndex Index => Services.GetRequiredService<SearchIndexRegistry>().Find(TestArticles.Key)!;

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

        // The engine relies on these text search configurations (stock PostgreSQL 16 ships them).
        (await AdminScalarAsync<long>("SELECT count(*) FROM pg_ts_config WHERE cfgname IN ('simple', 'english', 'arabic')"))
            .Should().Be(3, "the PostgreSQL engine analyzes with the simple, english and arabic configurations");

        // 2. The application role: DML only, not a superuser and not the table owner, so RLS applies.
        await AdminExecAsync(
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS; " +
            $"GRANT USAGE ON SCHEMA search TO {AppRole}; " +
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA search TO {AppRole};");
        _appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        // 3. The real module, as the app role.
        _provider = BuildProvider();
    }

    /// <summary>A provider of the real module; <paramref name="overrides"/> replaces services (e.g. a failing engine).</summary>
    public ServiceProvider BuildProvider(Action<IServiceCollection>? overrides = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DatabaseDefaults.ConnectionStringName}"] = _appConnection,
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
        services.AddSingleton(Articles);

        services.AddSearchModule(configuration);
        services.AddSearchIndex<InMemoryArticleSource>(TestArticles.Definition);

        overrides?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static TenantInfo Tenant(Guid id) => new(id, "t" + id.ToString("N")[..8], "Tenant");

    /// <summary>Work in the scope of <paramref name="tenantId"/> (and optionally a restricted branch scope).</summary>
    public async Task<T> RunAsync<T>(
        Guid tenantId, Func<IServiceProvider, Task<T>> action, IReadOnlyList<Guid>? branches = null, IServiceProvider? services = null)
    {
        using (TenantScope.Begin(Tenant(tenantId), branches is null ? null : new BranchScope(branches)))
        {
            await using var scope = (services ?? Services).CreateAsyncScope();
            return await action(scope.ServiceProvider);
        }
    }

    public Task<T> RunAsync<T>(Guid tenantId, Func<ISearchEngine, Task<T>> action, IReadOnlyList<Guid>? branches = null) =>
        RunAsync(tenantId, sp => action(sp.GetRequiredService<ISearchEngine>()), branches);

    /// <summary>One request of a member of <paramref name="tenantId"/>: tenant scope + fresh DI scope, like the middleware.</summary>
    public Task<T> SendAsync<T>(Guid tenantId, IRequest<T> request, IReadOnlyList<Guid>? branches = null, IServiceProvider? services = null) =>
        RunAsync(tenantId, sp => sp.GetRequiredService<ISender>().Send(request), branches, services);

    /// <summary>Marks the test index of <paramref name="tenantId"/> as built (the indexing pipeline does this in production).</summary>
    public Task MarkBuiltAsync(Guid tenantId) =>
        RunAsync(tenantId, async sp =>
        {
            var db = sp.GetRequiredService<SearchDbContext>();
            var now = DateTime.UtcNow;
            var state = await db.IndexStates.FirstOrDefaultAsync(s => s.IndexKey == TestArticles.Key);
            if (state is null)
            {
                state = SearchIndexState.Create(TestArticles.Key, now);
                db.IndexStates.Add(state);
            }

            if (!state.CanAnswer)
            {
                state.TryStartBuild(1, now, TimeSpan.FromMinutes(5)).Should().BeTrue();
                state.CompleteBuild(1, 0, Index.Fingerprint, now);
            }

            await db.SaveChangesAsync();
            return true;
        });

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
