using Microsoft.EntityFrameworkCore.Design;

namespace Search.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add ... --context SearchDbContext</c>. The migrations history
/// table matches the runtime one (<c>UseModuleMigrationsHistory</c>).
/// </summary>
public class SearchDbContextFactory : IDesignTimeDbContextFactory<SearchDbContext>
{
    public SearchDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseNpgsql(DatabaseDefaults.DesignTimeConnectionString,
                b => b.UseModuleMigrationsHistory<SearchDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SearchDbContext(options);
    }
}
