using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Reusable Testcontainers Postgres fixture.
///
/// Two ways to use it:
/// <list type="number">
///   <item>Full host: <see cref="E2ETestFixture"/> wraps it and boots the real Api
///   (all modules, all migrations) via <see cref="E2ETestFactory"/>.</item>
///   <item>Module-level integration tests without HTTP: use it as an xUnit class/collection
///   fixture, then call <see cref="AddModuleDbContext{TContext}"/> on your own
///   <see cref="ServiceCollection"/> to wire a module's real DbContext (Npgsql + snake_case +
///   the audit interceptor, exactly like production) and <see cref="MigrateAsync{TContext}"/>
///   to apply that module's real migrations.</item>
/// </list>
/// The domain-event dispatch interceptor is deliberately not registered by
/// <see cref="AddModuleDbContext{TContext}"/>; add it yourself if a test needs events.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Matches AddCustomDbContext in Shared.Core so DateTime handling is identical.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Registers <typeparamref name="TContext"/> against the container the same way production
    /// does. Callers must also register <c>IHttpContextService</c> (the audit interceptor's
    /// dependency) — a simple fake is enough.
    /// </summary>
    public IServiceCollection AddModuleDbContext<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        var connectionString = ConnectionString;

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
            options.UseNpgsql(
                    connectionString,
                    db => db.MigrationsAssembly(typeof(TContext).Assembly.GetName().Name))
                .UseSnakeCaseNamingConvention();
            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        return services;
    }

    /// <summary>Applies the module's real migrations to the container database.</summary>
    public static async Task MigrateAsync<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
    }
}
