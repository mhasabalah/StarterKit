using Shared.Core.Data;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations). The runtime connection comes from
/// AddCustomDbContext(configuration, "starterkit"). The migrations history table is left at the EF
/// default so design-time and runtime agree on it.
/// </summary>
public class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();

        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";

        optionsBuilder.UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<CatalogDbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();

        return new CatalogDbContext(optionsBuilder.Options);
    }
}
