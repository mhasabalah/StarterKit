using Shared.Core.Data;

namespace Notification.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("notification");
        builder.ToSnakeCaseTables();
        builder.ApplyConfiguration(new NotificationLogConfiguration());
        builder.ApplyConfiguration(new UserNotificationPreferenceConfiguration());
    }
}

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = config.GetConnectionString("starterkit")
            ?? throw new InvalidOperationException("Connection string 'starterkit' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(
                connectionString,
                b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention();

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
