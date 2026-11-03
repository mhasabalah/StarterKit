using Shared.Core.Data;
using Microsoft.EntityFrameworkCore.Design;

namespace Subscriptions.Data;

public class SubscriptionsDbContextFactory : IDesignTimeDbContextFactory<SubscriptionsDbContext>
{
    public SubscriptionsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SubscriptionsDbContext>();

        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";

        optionsBuilder.UseNpgsql(connectionString, b =>
            b.UseModuleMigrationsHistory<SubscriptionsDbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();

        return new SubscriptionsDbContext(optionsBuilder.Options);
    }
}
