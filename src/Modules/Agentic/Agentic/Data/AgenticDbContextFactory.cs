using Microsoft.EntityFrameworkCore.Design;

namespace Agentic.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add ... --context AgenticDbContext</c>. The migrations history
/// table matches the runtime one (<c>UseModuleMigrationsHistory</c>).
/// </summary>
public class AgenticDbContextFactory : IDesignTimeDbContextFactory<AgenticDbContext>
{
    public AgenticDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgenticDbContext>()
            .UseNpgsql(DatabaseDefaults.DesignTimeConnectionString,
                b => b.UseModuleMigrationsHistory<AgenticDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AgenticDbContext(options);
    }
}
