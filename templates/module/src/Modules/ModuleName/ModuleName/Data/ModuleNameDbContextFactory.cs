using Microsoft.EntityFrameworkCore.Design;

namespace ModuleName.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add ... --context ModuleNameDbContext</c>. The migrations history
/// table matches the runtime one (<c>UseModuleMigrationsHistory</c>).
/// </summary>
public class ModuleNameDbContextFactory : IDesignTimeDbContextFactory<ModuleNameDbContext>
{
    public ModuleNameDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ModuleNameDbContext>()
            .UseNpgsql(DatabaseDefaults.DesignTimeConnectionString,
                b => b.UseModuleMigrationsHistory<ModuleNameDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ModuleNameDbContext(options);
    }
}
