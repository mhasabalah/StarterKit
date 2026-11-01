namespace Shared.Email.Persistence;

public class EmailDbContext(DbContextOptions<EmailDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("shared");
        builder.ToSnakeCaseTables();

        builder.Entity<EmailLog>(entity =>
        {
            entity.ToTable("email_log");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Culture).HasConversion<string>();
            entity.Property(x => x.MessageJson).HasColumnType("jsonb");

            entity.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("idx_email_log_status_created");
        });
    }
}

public class EmailDbContextFactory : IDesignTimeDbContextFactory<EmailDbContext>
{
    public EmailDbContext CreateDbContext(string[] args)
    {
        // Run from src/Api/ so appsettings.json is in the current directory
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = config.GetConnectionString("starterkit")
            ?? throw new InvalidOperationException("Connection string 'starterkit' not found in appsettings.json");

        var optionsBuilder = new DbContextOptionsBuilder<EmailDbContext>();
        optionsBuilder.UseNpgsql(
                connectionString,
                b => b.MigrationsHistoryTable("__email_migrations_history", "shared"))
            .UseSnakeCaseNamingConvention();

        return new EmailDbContext(optionsBuilder.Options);
    }
}
