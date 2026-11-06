using Microsoft.EntityFrameworkCore.Design;

namespace DataTransfer.Data;

/// <summary>Design-time factory for <c>dotnet ef</c> (migrations). The connection string is the local-dev default.</summary>
public class DataTransferDbContextFactory : IDesignTimeDbContextFactory<DataTransferDbContext>
{
    public DataTransferDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DataTransferDbContext>();
        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<DataTransferDbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new DataTransferDbContext(optionsBuilder.Options);
    }
}
