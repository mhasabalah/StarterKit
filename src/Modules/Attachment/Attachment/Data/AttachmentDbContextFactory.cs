using Shared.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Attachment.Data;

/// <summary>
/// Design-time factory for AttachmentDbContext to support EF Core migrations
/// </summary>
public class AttachmentDbContextFactory : IDesignTimeDbContextFactory<AttachmentDbContext>
{
    public AttachmentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AttachmentDbContext>();

        // Use a default connection string for migrations
        // This will be overridden at runtime by the actual configuration
        var connectionString = "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";

        optionsBuilder.UseNpgsql(
            connectionString,
            b => b.UseModuleMigrationsHistory<AttachmentDbContext>());

        // Add snake_case naming convention
        optionsBuilder.UseSnakeCaseNamingConvention();

        return new AttachmentDbContext(optionsBuilder.Options);
    }
}
