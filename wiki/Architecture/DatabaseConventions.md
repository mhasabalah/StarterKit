# Database Conventions

## Naming Conventions

### PostgreSQL snake_case
All database objects use **snake_case** naming via `EFCore.NamingConventions`:

```csharp
// Entity
public class ProductCategory { }

// Database table
product_category

// Property
public string ArabicName { get; set; }

// Database column
arabic_name
```

### Configuration
```csharp
// In DbContext or Startup
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention(); // EFCore.NamingConventions
}
```

## Schema Organization

### Module-Based Schemas
Each module has its own schema:

```sql
-- Identity module
CREATE SCHEMA identity;

-- Account module
CREATE SCHEMA account;

-- Catalog module
CREATE SCHEMA catalog;

-- Catalog module
CREATE SCHEMA catalog;
```

### EF Core Configuration
```csharp
public class CatalogDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("account");
        
        // Entity configurations
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
```

## Table Design

### Primary Keys
```csharp
// Always use Guid for IDs
public class Product : BaseEntity
{
    public Guid Id { get; set; } // Primary key
}
```

```sql
-- Database
CREATE TABLE catalog.products (
    id UUID PRIMARY KEY,
    arabic_name VARCHAR(200) NOT NULL,
    english_name VARCHAR(200) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ
);
```

### Timestamps (UTC only)
Npgsql runs in its default (non-legacy) mode: `DateTime` and `DateTimeOffset` map to `timestamptz`.
- Write `DateTime` values with `Kind=Utc` (`TimeProvider.GetUtcNow().UtcDateTime`, `DateTime.UtcNow`,
  `new DateTime(..., DateTimeKind.Utc)`). `Kind=Local`/`Unspecified` throws on save.
- Write `DateTimeOffset` values with offset 0: normalize client input with `ToUniversalTime()` before storing or
  using it as a query parameter.
- Never turn on `Npgsql.EnableLegacyTimestampBehavior`.

### Foreign Keys
```csharp
public class Product : NamedAggregate
{
    public Guid ProfileId { get; private set; } // Foreign key
    public Profile? Profile { get; private set; } // Navigation
}
```

```sql
CREATE TABLE catalog.products (
    id UUID PRIMARY KEY,
    profile_id UUID NOT NULL,
    CONSTRAINT fk_products_profile FOREIGN KEY (profile_id) 
        REFERENCES account.profiles(id) ON DELETE CASCADE
);
```

### Indexes
```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Unique index
        builder.HasIndex(a => a.Email).IsUnique();
        
        // Composite index
        builder.HasIndex(a => new { a.ProfileId, a.IsActive });
        
        // Include columns for covering index
        builder.HasIndex(a => a.ArabicName)
            .IncludeProperties(a => new { a.EnglishName, a.Email });
    }
}
```

## Entity Configuration

### Fluent API
```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Table
        builder.ToTable("products");
        
        // Primary key
        builder.HasKey(a => a.Id);
        
        // Properties
        builder.Property(a => a.ArabicName)
            .HasMaxLength(200)
            .IsRequired();
        
        builder.Property(a => a.Email)
            .HasMaxLength(100);
        
        // Value object
        builder.OwnsOne(a => a.Address, addressBuilder =>
        {
            addressBuilder.Property(ad => ad.Street).HasColumnName("address_street");
            addressBuilder.Property(ad => ad.City).HasColumnName("address_city");
        });
        
        // Relationships
        builder.HasOne(a => a.Profile)
            .WithMany()
            .HasForeignKey(a => a.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasMany(a => a.CategoryAssignments)
            .WithOne()
            .HasForeignKey(asp => asp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Indexes
        builder.HasIndex(a => a.Email).IsUnique();
        
        // Query filters
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
```

### Relationships

#### One-to-Many
```csharp
// Parent
public class Product
{
    private readonly List<ProductCategory> _categoryAssignments = new();
    public IReadOnlyCollection<ProductCategory> CategoryAssignments => 
        _categoryAssignments.AsReadOnly();
}

// Child
public class ProductCategory
{
    public Guid ProductId { get; private set; }
    public Product? Product { get; private set; }
}

// Configuration
builder.HasMany(a => a.CategoryAssignments)
    .WithOne(asp => asp.Product)
    .HasForeignKey(asp => asp.ProductId);
```

#### Many-to-Many
```csharp
// Using explicit join entity
public class ProductCategory
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    
    public Guid CategoryId { get; set; }
    public ServicePlace? ServicePlace { get; set; }
}

// Configuration
builder.HasKey(asp => new { asp.ProductId, asp.CategoryId });
```

## Migrations

### Creating Migrations
```powershell
# Navigate to module project
cd src\Modules\Account\Account

# Add migration
dotnet ef migrations add InitialCreate --context CatalogDbContext --output-dir Data/Migrations

# Update database
dotnet ef database update --context CatalogDbContext
```

### Migration Best Practices
```csharp
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Create schema
        migrationBuilder.EnsureSchema("account");
        
        // Create tables
        migrationBuilder.CreateTable(
            name: "products",
            schema: "account",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                arabic_name = table.Column<string>(maxLength: 200, nullable: false),
                english_name = table.Column<string>(maxLength: 200, nullable: false),
                profile_id = table.Column<Guid>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_products", x => x.id);
                table.ForeignKey(
                    name: "fk_products_profile",
                    column: x => x.profile_id,
                    principalSchema: "account",
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
        
        // Create indexes
        migrationBuilder.CreateIndex(
            name: "ix_products_profile_id",
            schema: "account",
            table: "products",
            column: "profile_id");
    }
    
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "products",
            schema: "account");
    }
}
```

## Data Seeding

### Seed Configuration
```csharp
public class AccountDataSeeder
{
    public static async Task SeedAsync(CatalogDbContext context)
    {
        // Check if data exists
        if (await context.Profiles.AnyAsync())
            return;
        
        // Seed data
        var profiles = InitialData.Profiles;
        await context.Profiles.AddRangeAsync(profiles);
        
        var products = InitialData.Products;
        await context.Products.AddRangeAsync(products);
        
        await context.SaveChangesAsync();
    }
}
```

### Initial Data
```csharp
public static class InitialData
{
    public static List<Profile> Profiles => new()
    {
        Profile.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Admin Profile",
            ProfileType.Admin
        ),
        Profile.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Product Profile",
            ProfileType.Product
        )
    };
    
    public static List<Product> Products => new()
    {
        Product.Create(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "أحمد محمد",
            "Ahmed Mohammed",
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        )
    };
}
```

## Query Patterns

### Include Related Data
```csharp
// Eager loading
var product = await context.Products
    .Include(a => a.Profile)
    .Include(a => a.CategoryAssignments)
    .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

// Split query (for multiple collections)
var product = await context.Products
    .Include(a => a.CategoryAssignments)
    .Include(a => a.Documents)
    .AsSplitQuery()
    .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
```

### Projection
```csharp
// Select specific fields
var productDtos = await context.Products
    .Where(a => a.IsActive)
    .Select(a => new ProductDto
    {
        Id = a.Id,
        ArabicName = a.ArabicName,
        EnglishName = a.EnglishName
    })
    .ToListAsync(cancellationToken);
```

### Pagination
```csharp
var products = await context.Products
    .Where(a => a.IsActive)
    .OrderBy(a => a.ArabicName)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

## Query Filters

### Global Filters
```csharp
// Every filter is NAMED (EF Core 10) so one can be switched off without dropping the others.
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.FilterSoftDeletedProperties();   // "SoftDelete" on every IEntity
        // module-specific: builder.Entity<X>().HasQueryFilter(QueryFilterNames.Lifecycle, x => ...);
        ApplyTenantFilters(builder);             // "Tenant" (+ "Branch") - see wiki/MultiTenancy
    }
}

// Admin/restore/purge paths: ignore soft delete & lifecycle, KEEP tenant isolation.
// The parameterless IgnoreQueryFilters() is forbidden (architecture test) - it would drop the tenant filter too.
var deletedProducts = await context.Products
    .IgnoreNonIsolationFilters()
    .Where(a => a.IsDeleted)
    .ToListAsync();
```

## Transactions

### Unit of Work
```csharp
public async Task<Result> Handle(CreateProductCommand command, CancellationToken ct)
{
    // DbContext.SaveChangesAsync is transactional by default
    var product = Product.Create(...);
    repository.Add(product);
    await repository.SaveChangesAsync(ct);
    
    return Result.Success();
}
```

### Explicit Transactions
```csharp
public async Task<Result> Handle(ComplexCommand command, CancellationToken ct)
{
    await using var transaction = await context.Database.BeginTransactionAsync(ct);
    
    try
    {
        // Multiple operations
        var product = Product.Create(...);
        context.Products.Add(product);
        await context.SaveChangesAsync(ct);
        
        var profile = Profile.Create(...);
        context.Profiles.Add(profile);
        await context.SaveChangesAsync(ct);
        
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

## Performance Tips

### AsNoTracking for Read-Only
```csharp
var products = await context.Products
    .AsNoTracking() // Read-only, better performance
    .ToListAsync(cancellationToken);
```

### Compiled Queries
```csharp
private static readonly Func<CatalogDbContext, Guid, Task<Product?>> GetProductById =
    EF.CompileAsyncQuery((CatalogDbContext context, Guid id) =>
        context.Products
            .Include(a => a.Profile)
            .FirstOrDefault(a => a.Id == id));

// Usage
var product = await GetProductById(context, productId);
```

### Batch Operations
```csharp
// Use AddRange instead of multiple Add calls
context.Products.AddRange(products);
await context.SaveChangesAsync();
```

## Next Steps
- [API Guidelines](ApiGuidelines.md)
- [Testing Strategies](../Testing/TestingStrategies.md)
