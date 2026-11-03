using Microsoft.EntityFrameworkCore.Design;

namespace Tenancy.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations). The runtime connection comes from
/// AddCustomDbContext(configuration, "starterkit"); the migrations history table matches the runtime one.
/// </summary>
public class TenancyDbContextFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenancyDbContext>();

        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";

        optionsBuilder.UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<TenancyDbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();

        return new TenancyDbContext(optionsBuilder.Options);
    }
}
