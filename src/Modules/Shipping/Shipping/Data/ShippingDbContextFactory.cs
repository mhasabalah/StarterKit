using Microsoft.EntityFrameworkCore.Design;

namespace Shipping.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add ... --context ShippingDbContext</c>. The migrations history
/// table matches the runtime one (<c>UseModuleMigrationsHistory</c>).
/// </summary>
public class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShippingDbContext>()
            .UseNpgsql(DatabaseDefaults.DesignTimeConnectionString,
                b => b.UseModuleMigrationsHistory<ShippingDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ShippingDbContext(options);
    }
}
