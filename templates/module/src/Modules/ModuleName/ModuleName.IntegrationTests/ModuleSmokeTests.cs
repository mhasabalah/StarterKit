using Microsoft.Extensions.DependencyInjection;
using ModuleName.Data;
#if (EXAMPLE || OUTBOX)
using Shared.Core.Data;
#endif

namespace ModuleName.IntegrationTests;

[Collection(ModuleNamePostgresCollection.Name)]
public class ModuleSmokeTests(ModuleNamePostgresFixture fx)
{
    [Fact]
    public async Task The_module_context_connects_as_the_app_role()
    {
        await using var scope = fx.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<ModuleNameDbContext>().Database.CanConnectAsync())
            .Should().BeTrue();
    }
#if (EXAMPLE || OUTBOX)

    [Fact]
    public async Task The_migrations_are_applied_into_the_modules_own_history_table()
    {
        var table = MigrationsHistory.TableName<ModuleNameDbContext>();

        (await fx.AdminScalarAsync<long>($"select count(*) from {MigrationsHistory.Schema}.\"{table}\""))
            .Should().BeGreaterThan(0);
    }
#endif
}
