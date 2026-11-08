using Api.E2ETests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>The API host with <c>MultiTenancy:Enabled=true</c> (options are read lazily, so an in-memory override works).</summary>
public sealed class MultiTenantE2ETestFactory(string connectionString) : E2ETestFactory(connectionString)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MultiTenancy:Enabled"] = "true",
            ["MultiTenancy:Resolution:RootDomain"] = "api.example.com",
            ["MultiTenancy:Cache:LocalSeconds"] = "0", // observe membership changes immediately
            ["MultiTenancy:Database:MigrateDedicatedOnStartup"] = "false",
            ["MultiTenancy:Invitations:ReturnTokenInResponse"] = "true" // the flow accepts the token directly
        }));
    }
}

/// <summary>Own Postgres + own multi-tenant host (separate from the single-tenant E2E collection).</summary>
public sealed class MultiTenantE2EFixture : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres = new();

    public MultiTenantE2ETestFactory Factory { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new MultiTenantE2ETestFactory(_postgres.ConnectionString);
        _ = Factory.Services; // boot: migrations + Default tenant seed
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A client authenticated as <paramref name="userId"/>, optionally with a tenant-bound session.</summary>
    public HttpClient Client(Guid userId, Guid? tenantId = null, string? email = null)
    {
        var client = Factory.CreateClientAs(userId);
        if (tenantId is { } tid)
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tid.ToString());
        if (email is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class MultiTenantE2ECollection : ICollectionFixture<MultiTenantE2EFixture>
{
    public const string Name = "E2E multi-tenant";
}
