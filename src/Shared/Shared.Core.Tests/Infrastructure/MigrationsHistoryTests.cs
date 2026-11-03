using Microsoft.EntityFrameworkCore;
using Shared.Core.Data;

namespace Shared.Core.Tests.Infrastructure;

public sealed class MigrationsHistoryTests
{
    private sealed class CatalogDbContext : DbContext;
    private sealed class ApplicationIdentityDbContext : DbContext;
    private sealed class Weird : DbContext;

    [Fact]
    public void Each_context_gets_its_own_snake_case_history_table_in_public()
    {
        MigrationsHistory.TableName<CatalogDbContext>().Should().Be("__catalog_migrations_history");
        MigrationsHistory.TableName<ApplicationIdentityDbContext>()
            .Should().Be("__application_identity_migrations_history");
        MigrationsHistory.TableName<Weird>().Should().Be("__weird_migrations_history");
        MigrationsHistory.Schema.Should().Be("public");
    }
}
