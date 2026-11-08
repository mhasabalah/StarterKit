using System.Security.Claims;
using Catalog.Data;
using Catalog.Data.Seed;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Shared.Core.Localization.Services;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Catalog.IntegrationTests;

/// <summary>
/// One real PostgreSQL (Testcontainers, postgres:16-alpine) for the whole collection, wired with the REAL
/// module registration (<c>AddCatalogModule</c>): real DbContext through Shared.Core AddCustomDbContext
/// (Npgsql + snake_case + legacy-timestamp switch + AuditableEntityInterceptor + DispatchDomainEventsInterceptor),
/// real mediator pipeline (validation, concurrency, domain-event dispatch), real repositories and services.
/// Faked boundary: the HTTP user (<see cref="FakeHttpContextService"/>). No bus: integration events stay in the
/// Catalog outbox table, which the tests assert on.
/// The schema comes from the module's migrations (<c>MigrateAsync</c>).
/// Tests share the database; each test uses fresh owners / SKUs so they stay independent.
/// </summary>
public sealed class CatalogPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public FakeHttpContextService Http { get; } = new();
    public IServiceProvider Services => _provider;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString(),
                ["Catalog:SoftDeleteRetentionDays"] = "30",
                ["Catalog:PurgeBatchSize"] = "100"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IEnumLocalizationService, EnumLocalizationService>();
        services.AddSingleton<IHttpContextService>(Http);
        services.AddCatalogModule(configuration);

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        // The real migrations (not EnsureCreated): some constraints exist only there (e.g. per-tenant unique
        // name indexes on owned types, RLS policies).
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetServices<Shared.Core.Data.Seed.IDataSeeder>()
            .OfType<CatalogDataSeeder>().Single().SeedAllAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Sends a request through the real pipeline in a fresh scope (= a new HTTP request) as the given user.</summary>
    public async Task<TResponse> SendAs<TResponse>(Guid? userId, IRequest<TResponse> request, bool admin = false)
    {
        Http.SetUser(userId, admin);
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public Task<TResponse> SendAsAdmin<TResponse>(IRequest<TResponse> request) =>
        SendAs(TestIds.Admin, request, admin: true);

    /// <summary>Direct DB access in a fresh scope (seeding / verification).</summary>
    public async Task<T> WithDb<T>(Func<CatalogDbContext, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    }

    public async Task WithScope(Func<IServiceProvider, Task> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogPostgresCollection : ICollectionFixture<CatalogPostgresFixture>
{
    public const string Name = "catalog-postgres";
}

public static class TestIds
{
    public static readonly Guid Admin = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
}

/// <summary>Mutable "current request user" shared by the interceptor, handlers and services.</summary>
public sealed class FakeHttpContextService : IHttpContextService
{
    private Guid? _userId;
    private string[] _roles = [];

    public void SetUser(Guid? userId, bool admin)
    {
        _userId = userId;
        _roles = userId is null ? [] : admin ? [RoleConstants.Admin] : [RoleConstants.User];
    }

    public string? GetUserId() => _userId?.ToString();
    public long? GetUserIdAsLong() => null;
    public Guid? GetUserIdAsGuid() => _userId;
    public string? GetUserName() => _userId?.ToString();
    public string? GetEmail() => null;
    public IEnumerable<string> GetRoles() => _roles;
    public bool IsAuthenticated() => _userId is not null;
    public string? GetClaimValue(string claimType) => null;
    public IEnumerable<string> GetClaimValues(string claimType) => [];
    public IEnumerable<Claim> GetAllClaims() => [];
    public bool HasClaim(string claimType, string? claimValue = null) => false;
    public string? GetIpAddress() => null;
    public string? GetUserAgent() => null;
    public string? GetRequestPath() => null;
    public string? GetRequestMethod() => null;
    public string? GetCorrelationId() => null;
    public string? GetHeaderValue(string headerKey) => null;
    public string? GetHost() => null;
    public string? GetScheme() => null;
    public string? GetRequestUrl() => null;
    public string? GetTenantId() => null;
}
