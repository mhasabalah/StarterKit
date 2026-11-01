namespace Catalog.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : AppDbContextBase(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("catalog");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}
