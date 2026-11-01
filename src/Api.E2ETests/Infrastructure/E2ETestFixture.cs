using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Spins up one Postgres container (<see cref="PostgresContainerFixture"/>) and one booted
/// Api host for the whole e2e run. Booting the host applies every module's migrations
/// against the fresh database, validating migrations end-to-end.
/// </summary>
public class E2ETestFixture : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres = new();

    public E2ETestFactory Factory { get; private set; } = default!;

    /// <summary>Creates a client authenticated as the given user/roles.</summary>
    public HttpClient CreateClientAs(Guid userId, params string[] roles)
        => Factory.CreateClientAs(userId, roles);

    /// <summary>Creates an anonymous client (no auth headers).</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateAnonymousClient();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();

        Factory = new E2ETestFactory(_postgres.ConnectionString);

        // Force host construction so migrations apply now (surfaces startup failures early).
        _ = Factory.Services;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>Runs an action with a fresh DI scope (for direct DB seeding/verification).</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider);
    }
}

[CollectionDefinition(E2ECollection.Name)]
public class E2ECollection : ICollectionFixture<E2ETestFixture>
{
    public const string Name = "E2E";
}
