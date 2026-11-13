using Microsoft.Extensions.DependencyInjection;
using Agentic.Data;
using Shared.Core.Data;

namespace Agentic.IntegrationTests;

[Collection(AgenticPostgresCollection.Name)]
public class ModuleSmokeTests(AgenticPostgresFixture fx)
{
    [Fact]
    public async Task The_module_context_connects_as_the_app_role()
    {
        await using var scope = fx.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<AgenticDbContext>().Database.CanConnectAsync())
            .Should().BeTrue();
    }

    [Fact]
    public async Task The_migrations_are_applied_into_the_modules_own_history_table()
    {
        var table = MigrationsHistory.TableName<AgenticDbContext>();

        (await fx.AdminScalarAsync<long>($"select count(*) from {MigrationsHistory.Schema}.\"{table}\""))
            .Should().BeGreaterThan(0);
    }
}
