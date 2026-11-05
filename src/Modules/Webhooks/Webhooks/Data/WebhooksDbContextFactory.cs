using Microsoft.EntityFrameworkCore.Design;

namespace Webhooks.Data;

public class WebhooksDbContextFactory : IDesignTimeDbContextFactory<WebhooksDbContext>
{
    public WebhooksDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WebhooksDbContext>();
        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<WebhooksDbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new WebhooksDbContext(optionsBuilder.Options);
    }
}
