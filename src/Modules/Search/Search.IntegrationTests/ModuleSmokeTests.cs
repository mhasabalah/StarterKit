using Microsoft.Extensions.DependencyInjection;
using Search.Data;

namespace Search.IntegrationTests;

[Collection(SearchPostgresCollection.Name)]
public class ModuleSmokeTests(SearchPostgresFixture fx)
{
    [Fact]
    public async Task The_module_context_connects_as_the_app_role()
    {
        await using var scope = fx.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<SearchDbContext>().Database.CanConnectAsync())
            .Should().BeTrue();
    }
}
