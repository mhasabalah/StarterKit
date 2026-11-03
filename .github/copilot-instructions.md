# StarterKit Copilot Agent Instructions

You are a senior .NET architect and developer working on the **StarterKit** project, a modular monolith ASP.NET Core 9 application.

## Core Architecture Rules

### Technology Stack
- **.NET 9.0** with C# 12+ features (file-scoped namespaces, primary constructors, records)
- **Entity Framework Core 9** with PostgreSQL (snake_case naming convention via EFCore.NamingConventions)
- **MediatR** for CQRS pattern
- **Carter** for minimal API endpoints
- **FluentValidation** for validation
- **Mapster** for object mapping
- **MassTransit** for messaging
- **.NET Aspire** for orchestration
- **Microsoft.Extensions.Logging + OpenTelemetry** for logging (Seq, Aspire dashboard, Loki)
- **Redis** for caching
- **OpenTelemetry** for tracing

### Architectural Patterns

**1. Vertical Slice Architecture**: Each feature is self-contained in its own folder with handler, endpoint, and validator.

**2. Clean Architecture + DDD**: 
- Domain models in `Domains/` layer
- Data access in `Data/` layer  
- Contracts in separate `.Contracts` project

**3. Modular Monolith**: Each module (Catalog, Catalog, etc.) is independent with its own:
- DbContext
- Repository implementations
- Domain models
- Features
- Module registration class

## The Catalog Module: Your Reference Template

**ALWAYS** use the Catalog module structure as your reference when creating new features or modules:

```
Catalog/
├── Catalog/                                    # Main implementation
│   ├── Domains/                               # Domain layer
│   │   ├── Products/                            # Aggregate root
│   │   │   ├── Models/
│   │   │   │   ├── Product.cs                   # Entity with factory methods
│   │   │   │   └── ProductCategory.cs       # Child entity
│   │   │   ├── Features/                      # Vertical slices
│   │   │   │   ├── CreateProduct/
│   │   │   │   │   ├── CreateProductHandler.cs  # MediatR handler + validator
│   │   │   │   │   └── CreateProductEndPoint.cs # Carter endpoint
│   │   │   │   ├── UpdateProduct/
│   │   │   │   ├── DeleteProduct/
│   │   │   │   ├── GetProducts/                 # Query feature
│   │   │   │   ├── GetProductById/
│   │   │   │   └── GetProductLookups/
│   │   │   ├── Events/                        # Domain events
│   │   │   │   ├── ProductCreatedEvent.cs
│   │   │   │   └── ProductUpdatedEvent.cs
│   │   │   ├── EventHandler/                  # Event handlers
│   │   │   ├── Exceptions/                    # Domain exceptions
│   │   │   │   └── ProductNotFoundException.cs
│   │   │   └── Mappings/                      # Mapster configs
│   │   ├── Companies/
│   │   ├── Profiles/
│   │   └── ... (other aggregates)
│   ├── Data/                                  # Data access layer
│   │   ├── Repository/
│   │   │   ├── Products/
│   │   │   │   ├── IProductRepository.cs        # Interface
│   │   │   │   └── ProductRepository.cs         # Implementation
│   │   │   └── ... (per aggregate)
│   │   ├── Configurations/                    # EF configurations
│   │   │   ├── ProductConfiguration.cs
│   │   │   └── ...
│   │   ├── Migrations/                        # EF migrations
│   │   ├── Seed/
│   │   │   ├── CatalogDataSeeder.cs
│   │   │   └── InitialData.cs
│   │   └── CatalogDbContext.cs
│   ├── CatalogModule.cs                       # Module registration
│   ├── GlobalUsings.cs                        # Global usings
│   └── Catalog.csproj
└── Catalog.Contracts/                         # Contracts
    ├── Products/
    │   ├── Commands/
    │   │   ├── CreateProductCommand.cs
    │   │   └── UpdateProductCommand.cs
    │   ├── Queries/
    │   │   ├── GetProductsQuery.cs
    │   │   └── GetProductByIdQuery.cs
    │   └── Dtos/
    │       ├── GetProductsDto.cs
    │       └── ProductDto.cs
    └── Catalog.Contracts.csproj
```

## Code Patterns You MUST Follow

### 1. Domain Entity with Factory Method

```csharp
public class Product : NamedAggregate<Guid>
{
    public Guid ProfileId { get; private set; }
    public Profile? Profile { get; private set; }
    
    private readonly List<ProductCategory> _categoryAssignments = new();
    public IReadOnlyCollection<ProductCategory> CategoryAssignments => 
        _categoryAssignments.AsReadOnly();
    
    // ✅ Factory method for creation
    public static Product Create(Guid id, string arabicName, string englishName, Guid profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(arabicName);
        ArgumentException.ThrowIfNullOrEmpty(englishName);
        
        var product = new Product
        {
            Id = id,
            ArabicName = arabicName,
            EnglishName = englishName,
            ProfileId = profileId
        };
        
        product.AddDomainEvent(new ProductCreatedEvent(product));
        return product;
    }
    
    // ✅ Domain logic methods
    public void Update(string arabicName, string englishName)
    {
        ArgumentException.ThrowIfNullOrEmpty(arabicName, nameof(arabicName));
        ArgumentException.ThrowIfNullOrEmpty(englishName, nameof(englishName));
        
        ArabicName = arabicName;
        EnglishName = englishName;
        
        AddDomainEvent(new ProductUpdatedEvent(this));
    }
    
    public void AssignToCategory(Guid categoryId, bool isActive = true)
    {
        if (!_categoryAssignments.Any(asp => asp.CategoryId == categoryId))
        {
            var assignment = ProductCategory.Create(Id, categoryId, isActive);
            _categoryAssignments.Add(assignment);
        }
    }
}
```

### 2. Command Handler with Validator

```csharp
// ✅ Validator defined in same file, before handler
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(e => e.ArabicName).NotEmpty().WithMessage("Arabic Name is required");
        RuleFor(e => e.EnglishName).NotEmpty().WithMessage("English Name is required");
        RuleFor(e => e.UserName).NotEmpty().WithMessage("User Name is required");
        RuleFor(e => e.Email).EmailAddress().When(e => !string.IsNullOrEmpty(e.Email));
    }
}

// ✅ Primary constructor pattern
public class CreateProductHandler(IProductRepository repository, IProfileRepository profileRepository)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        // Business logic validation
        var userNameExists = await profileRepository.ExistsByUserNameAsync(command.UserName, cancellationToken);
        if (userNameExists)
        {
            throw new InvalidOperationException($"User name '{command.UserName}' already exists.");
        }
        
        // Create related entity first
        var profile = Profile.Create(Guid.NewGuid(), ProfileType.Product, command.UserName, command.Email);
        await profileRepository.AddAsync(profile, cancellationToken);
        
        // Create main entity using factory method
        var product = Product.Create(Guid.NewGuid(), command.ArabicName, command.EnglishName, profile.Id);
        
        // Handle optional relationships
        if (command.CategoryIds?.Any() == true)
        {
            foreach (var categoryId in command.CategoryIds)
            {
                product.AssignToCategory(categoryId);
            }
        }
        
        await repository.AddAsync(product, cancellationToken);
        
        return new CreateProductResult(product.Id);
    }
}
```

### 3. Query Handler with Projection

```csharp
public class GetProductsHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<GetProductsResult> Handle(GetProductsQuery query, CancellationToken cancellationToken)
    {
        // ✅ Use LINQ for complex projections
        var queryable = from product in dbContext.Products
                        join profile in dbContext.Profiles on product.ProfileId equals profile.Id
                        let categorys = (from asp in dbContext.ProductCategorys
                                           join sp in dbContext.Categorys on asp.CategoryId equals sp.Id
                                           where asp.ProductId == product.Id
                                           select new CategoryDto(sp.Id, sp.ArabicName, sp.EnglishName, null)
                                          ).ToList()
                        select new GetProductsDto(
                            product.Id,
                            product.ArabicName,
                            product.EnglishName,
                            profile.UserName,
                            profile.Email,
                            categorys
                        );
        
        var totalCount = await queryable.LongCountAsync(cancellationToken);
        
        // ✅ Always paginate
        var productDtos = await queryable
            .Skip(query.PaginationRequest.PageSize * query.PaginationRequest.PageIndex)
            .Take(query.PaginationRequest.PageSize)
            .ToListAsync(cancellationToken);
        
        var result = new PaginatedResult<GetProductsDto>(
            query.PaginationRequest.PageIndex,
            query.PaginationRequest.PageSize,
            totalCount,
            productDtos);
        
        return new GetProductsResult(result);
    }
}
```

### 4. Carter Endpoint

```csharp
public record CreateProductRequest(
    string ArabicName,
    string EnglishName,
    string UserName,
    string? Email,
    List<Guid>? CategoryIds = null);

public record CreateProductResponse(Guid Id);

public class CreateProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/Products", async (CreateProductRequest request, ISender sender) =>
            {
                // ✅ Use Mapster for mapping
                var command = request.Adapt<CreateProductCommand>();
                var result = await sender.Send(command);
                var response = result.Adapt<CreateProductResponse>();
                
                return Results.Created($"api/products/{response.Id}", response);
            })
            .WithName("CreateProduct")
            .WithApiVersionSet(app.NewApiVersionSet("Products").Build())
            .Produces<ApiResponse<CreateProductResponse>>()
            .WithSummary("Create Product")
            .WithDescription("Create Product");
    }
}
```

### 5. Repository Implementation

```csharp
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task<Product> GetByIdAsync(Guid productId, bool asNoTracking = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaginatedResult<Product>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(string? userName = null, CancellationToken cancellationToken = default);
}

public class ProductRepository(CatalogDbContext dbContext) : IProductRepository
{
    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await dbContext.Products.AddAsync(product, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<Product> GetByIdAsync(Guid productId, bool asNoTracking = true, CancellationToken cancellationToken = default)
    {
        IQueryable<Product> query = dbContext.Products.Include(a => a.CategoryAssignments);
        
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }
        
        return await query.FirstOrDefaultAsync(a => a.Id == productId, cancellationToken) ??
               throw new ProductNotFoundException(productId);
    }
    
    public async Task<PaginatedResult<Product>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Products.Include(a => a.CategoryAssignments);
        var totalItems = await query.LongCountAsync(cancellationToken);
        
        var items = await query
            .Skip(request.PageSize * request.PageIndex)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        
        return new PaginatedResult<Product>(request.PageIndex, request.PageSize, totalItems, items);
    }
    
    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        dbContext.Products.Update(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<bool> DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await GetByIdAsync(productId, false, cancellationToken);
        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
```

### 6. EF Core Configuration

```csharp
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(a => a.Id);
        
        builder.Property(a => a.ArabicName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.EnglishName).IsRequired().HasMaxLength(200);
        
        builder.HasOne(a => a.Profile)
            .WithMany()
            .HasForeignKey(a => a.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasMany(a => a.CategoryAssignments)
            .WithOne()
            .HasForeignKey(asp => asp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## Naming Conventions (STRICT)

- **Interfaces**: `IProductRepository`, `IProfileRepository`
- **Private fields**: `_categoryAssignments`, `_repository`
- **Async methods**: `GetByIdAsync`, `CreateAsync`, `UpdateAsync`
- **Handlers**: `CreateProductHandler`, `GetProductsHandler`
- **Validators**: `CreateProductCommandValidator`
- **Endpoints**: `CreateProductEndPoint`, `GetProductsEndPoint`
- **Commands**: `CreateProductCommand`, `UpdateProductCommand`
- **Queries**: `GetProductsQuery`, `GetProductByIdQuery`
- **DTOs**: `GetProductsDto`, `ProductDto`, `CategoryDto`
- **Results**: `CreateProductResult`, `GetProductsResult`
- **Requests**: `CreateProductRequest`, `UpdateProductRequest`
- **Responses**: `CreateProductResponse`, `ProductResponse`
- **Events**: `ProductCreatedEvent`, `ProductUpdatedEvent`
- **Exceptions**: `ProductNotFoundException`, `InvalidProductException`

## Code Style Rules (from .editorconfig)

- ✅ **File-scoped namespaces**: `namespace Catalog.Domains.Products.Models;` (REQUIRED)
- ✅ **Primary constructors**: `public class Handler(IRepository repo) : IHandler`
- ✅ **var for built-in types**: `var product = new Product();`
- ✅ **Private fields with underscore**: `private readonly List<Product> _products = new();`
- ✅ **4 space indentation**
- ✅ **Max 100 characters per line**
- ✅ **Using directives outside namespace**
- ✅ **Braces on new line for all constructs**

## Module Registration Pattern

```csharp
// ModuleNameModule.cs
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register repositories
        services.AddScoped<IModeratorRepository, ModeratorRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        
        // Register MediatR, Mapster, Carter
        services.AddMediatR(CatalogRoot.AssemblyName);
        services.AddCustomMapster(CatalogRoot.AssemblyName);
        services.AddCarterWithAssemblies(CatalogRoot.AssemblyName);
        
        // Register DbContext
        services.AddCustomDbContext<CatalogDbContext>(configuration, "starterkit");
        services.AddScoped<IDataSeeder, CatalogDataSeeder>();
        
        return services;
    }
    
    public static IApplicationBuilder UseCatalogModule(this IApplicationBuilder app)
    {
        app.UseMigration<CatalogDbContext>();
        return app;
    }
}
```

## Common Commands

### Database Migrations
```powershell
# From src\Api directory
cd src\Api

# Add migration (specify context and output dir)
dotnet ef migrations add <MigrationName> --context CatalogDbContext --output-dir ..\Modules\Catalog\Catalog\Data\Migrations

# Apply migrations
dotnet ef database update --context CatalogDbContext

# Remove last migration
dotnet ef migrations remove --context CatalogDbContext
```

### Running the Application
```powershell
# Run with Aspire (includes all infrastructure)
cd src\Aspire\AppHost
dotnet run

# Run API directly with hot reload
cd src\Api
dotnet watch run
```

### Build and Format
```powershell
# Build solution
dotnet build

# Format code
dotnet format

# Verify formatting
dotnet format --verify-no-changes
```

## When Creating New Features

1. **Define Contracts First** (`.Contracts` project):
   - Command/Query record in `Commands/` or `Queries/`
   - Result record
   - DTO records in `Dtos/`

2. **Create Domain Logic** (main project):
   - Update entity with factory methods and domain logic
   - Create feature folder: `Features/[FeatureName]/`
   - Add handler with validator: `[FeatureName]Handler.cs`
   - Add endpoint: `[FeatureName]EndPoint.cs`

3. **Update Repository** (if needed):
   - Add method to interface
   - Implement in repository class

4. **Add EF Configuration** (if new entity):
   - Create configuration class: `[Entity]Configuration.cs`
   - Implement `IEntityTypeConfiguration<T>`

5. **Create Migration**:
   ```powershell
   dotnet ef migrations add Add[FeatureName] --context [Module]DbContext --output-dir ..\Modules\[Module]\[Module]\Data\Migrations
   ```

6. **Register in Module** (if new repository):
   - Add to `[Module]Module.cs`: `services.AddScoped<INewRepository, NewRepository>();`

## Critical Rules

- ❌ **NEVER** use `var` for complex types unless type is apparent
- ❌ **NEVER** make domain properties public setters (use `private set`)
- ❌ **NEVER** create entities with `new Entity()` (use factory methods)
- ❌ **NEVER** forget to raise domain events for important actions
- ❌ **NEVER** return tracking entities from queries (use `AsNoTracking()`)
- ❌ **NEVER** forget `CancellationToken` parameter
- ✅ **ALWAYS** validate command inputs with FluentValidation
- ✅ **ALWAYS** use primary constructors for handlers and repositories
- ✅ **ALWAYS** use records for DTOs, Commands, Queries, Requests, Responses
- ✅ **ALWAYS** paginate list queries
- ✅ **ALWAYS** use repository pattern (never DbContext directly in handlers)
- ✅ **ALWAYS** follow the Catalog module structure

## Project Structure Quick Reference

```
StarterKit/
├── src/
│   ├── Api/                    # API entry point
│   │   ├── Program.cs         # Module registration
│   │   └── appsettings.json
│   ├── Aspire/
│   │   ├── AppHost/           # Aspire orchestration
│   │   └── ServiceDefaults/
│   ├── Modules/
│   │   ├── Catalog/           # ⭐ YOUR REFERENCE MODULE
│   │   │   ├── Catalog/
│   │   │   └── Catalog.Contracts/
│   │   └── [OtherModules]/
│   └── Shared/
│       ├── Shared.Core/       # Base classes, extensions
│       ├── Shared.Contracts/  # CQRS interfaces
│       └── Shared.Messaging/  # MassTransit config
├── Directory.Build.props      # Global MSBuild properties
├── Directory.Packages.props   # Centralized package versions
├── .editorconfig             # Code style rules
├── AGENTS.md                 # Detailed documentation
└── README.md
```

## Remember

**When in doubt, look at the Catalog module!** It demonstrates all patterns correctly:
- Domain entities with factory methods
- Vertical slice features  
- Command/query handlers with validators
- Repository pattern
- Carter endpoints
- EF Core configurations
- Domain events

Your code should be **indistinguishable** from the existing Catalog module code in terms of style, structure, and patterns.
