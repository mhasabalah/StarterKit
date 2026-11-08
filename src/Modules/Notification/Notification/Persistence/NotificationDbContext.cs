using Shared.Core.Data;

namespace Notification.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();
    public DbSet<InboxNotification> InboxNotifications => Set<InboxNotification>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("notification");
        builder.ToSnakeCaseTables();
        builder.ApplyConfiguration(new NotificationLogConfiguration());
        builder.ApplyConfiguration(new UserNotificationPreferenceConfiguration());
        builder.ApplyConfiguration(new InboxNotificationConfiguration());
        builder.ApplyConfiguration(new PushDeviceConfiguration());
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

        var connectionString = config.GetConnectionString(Shared.Core.Data.DatabaseDefaults.ConnectionStringName)
            ?? throw new InvalidOperationException("Connection string 'starterkit' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(
                connectionString,
                b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention();

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
