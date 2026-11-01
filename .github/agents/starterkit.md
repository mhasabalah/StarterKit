---
name: StarterKit Agent
description: Senior .NET architect for StarterKit modular monolith with Clean Architecture and DDD patterns
---

# StarterKit Agent Instructions

## Project Overview

StarterKit is a modular monolith ASP.NET Core 9 application built with **Clean Architecture** and **Vertical Slice Architecture** principles. The system uses:

- **.NET 9.0** with C# 12+ features
- **Entity Framework Core 9** with PostgreSQL
- **MediatR** for CQRS pattern implementation
- **Carter** for minimal API endpoints
- **FluentValidation** for validation
- **Mapster** for object mapping
- **MassTransit** for messaging
- **.NET Aspire** for orchestration and observability
- **Serilog** with Seq for structured logging
- **Redis** for distributed caching
- **OpenTelemetry** for distributed tracing

### Architecture Pattern: Catalog Module as Reference

The **Catalog** module demonstrates the standard architecture pattern used across all modules:

```
Catalog/
├── Catalog/                          # Main module implementation
│   ├── Domains/                      # Domain layer (DDD)
│   │   ├── Products/
│   │   │   ├── Models/              # Domain entities (Product, ProductCategory)
│   │   │   ├── Features/            # Domain features (CreateProduct, UpdateProduct, DeleteProduct, GetProducts, etc.)
│   │   │   │   ├── CreateProduct/
│   │   │   │   │   ├── CreateProductHandler.cs      # Command handler with validation
│   │   │   │   │   └── CreateProductEndPoint.cs     # Carter endpoint
│   │   │   │   ├── UpdateProduct/
│   │   │   │   ├── DeleteProduct/
│   │   │   │   ├── GetProducts/
│   │   │   │   ├── GetProductById/
│   │   │   │   └── GetProductLookups/
│   │   │   ├── Events/              # Domain events (ProductCreatedEvent, ProductUpdatedEvent)
│   │   │   ├── Exceptions/          # Domain-specific exceptions
│   │   │   └── Mappings/            # Mapster configurations
│   │   ├── Companies/
│   │   ├── Governorates/
│   │   ├── Moderators/
│   │   ├── Profiles/
│   │   ├── Categorys/
│   │   └── ServiceRequirements/
│   ├── Data/                         # Data access layer
│   │   ├── Repository/              # Repository implementations per aggregate
│   │   │   ├── Products/
│   │   │   │   ├── ProductRepository.cs
│   │   │   │   └── IProductRepository.cs
│   │   │   ├── Companies/
│   │   │   ├── Profiles/
│   │   │   └── ...
│   │   ├── Configurations/          # EF Core entity configurations
│   │   │   ├── ProductConfiguration.cs
│   │   │   └── ...
│   │   ├── Migrations/              # EF Core migrations
│   │   ├── Seed/                    # Data seeding
│   │   │   ├── CatalogDataSeeder.cs
│   │   │   └── InitialData.cs
│   │   └── CatalogDbContext.cs      # Module DbContext
│   ├── CatalogModule.cs             # Module registration and configuration
│   ├── GlobalUsings.cs              # Global using directives
│   └── Catalog.csproj
└── Catalog.Contracts/                # Contract layer (DTOs, Commands, Queries)
    ├── Products/
    │   ├── Commands/                 # Command DTOs (CreateProductCommand, UpdateProductCommand)
    │   ├── Queries/                  # Query DTOs (GetProductsQuery, GetProductByIdQuery)
    │   └── Dtos/                     # Response DTOs (GetProductsDto, ProductDto)
    └── Catalog.Contracts.csproj
```

### Key Architectural Concepts

1. **Vertical Slice Architecture**: Each feature (e.g., CreateProduct, GetProducts) is self-contained with its handler and endpoint.
2. **Domain-Driven Design (DDD)**: Entities use factory methods (`Product.Create()`), encapsulation, and domain events.
3. **CQRS Pattern**: Commands and queries are separated with MediatR handlers.
4. **Repository Pattern**: Each aggregate root has its own repository interface and implementation.
5. **Carter Minimal APIs**: Endpoints are defined using Carter modules for clean, organized API definitions.
6. **Centralized Package Management**: All package versions are managed in `Directory.Packages.props`.

## Setup Commands

### Prerequisites

- .NET 9 SDK (version 9.0.101 or later)
- PostgreSQL 16+
- Redis (optional, for caching)
- Seq (optional, for log aggregation)
- Docker (optional, for containerized infrastructure)

### Installation

```powershell
# Restore all dependencies
dotnet restore

# Install EF Core tools globally (if not already installed)
dotnet tool install --global dotnet-ef

# Update EF Core tools (if already installed)
dotnet tool update --global dotnet-ef
```

### Database Setup

```powershell
# Navigate to the API project
cd src\Api

# Create database and apply migrations for all modules
dotnet ef database update --context CatalogDbContext

# Add a new migration (example for Catalog module)
dotnet ef migrations add <MigrationName> --context CatalogDbContext --output-dir ..\Modules\Catalog\Catalog\Data\Migrations

# Remove last migration if needed
dotnet ef migrations remove --context CatalogDbContext
```

### Running with Aspire (Recommended)

```powershell
# Navigate to AppHost
cd src\Aspire\AppHost

# Run with Aspire orchestration (starts API, PostgreSQL, Redis, Seq)
dotnet run

# Aspire Dashboard will be available at: https://localhost:15024 (or check console output)
```

### Running API Directly

```powershell
# Navigate to API project
cd src\Api

# Run the API
dotnet run

# API will be available at: https://localhost:5001 (or check console output)
# Swagger UI: https://localhost:5001/swagger
# Scalar API docs: https://localhost:5001/scalar/v1
```

## Development Workflow

### Starting Development

```powershell
# Option 1: Run with hot reload
cd src\Api
dotnet watch run

# Option 2: Run with Aspire (includes all infrastructure)
cd src\Aspire\AppHost
dotnet run
```

### Creating a New Module Feature (e.g., Product Feature)

Follow the **Catalog** module as a reference template:

1. **Define Contracts** (in `Catalog.Contracts/Products/`):
   ```
   - Commands/CreateProductCommand.cs
   - Queries/GetProductsQuery.cs
   - Dtos/GetProductsDto.cs
   ```

2. **Create Domain Models** (in `Catalog/Domains/Products/Models/`):
   ```csharp
   public class Product : NamedAggregate<Guid>
   {
       public static Product Create(...) { /* Factory method */ }
       public void Update(...) { /* Domain logic */ }
   }
   ```

3. **Implement Feature Handler** (in `Catalog/Domains/Products/Features/CreateProduct/`):
   ```csharp
   // CreateProductHandler.cs - Command handler with validator
   public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand> { }
   public class CreateProductHandler : ICommandHandler<CreateProductCommand, CreateProductResult> { }
   ```

4. **Create Carter Endpoint** (in `Catalog/Domains/Products/Features/CreateProduct/`):
   ```csharp
   // CreateProductEndPoint.cs
   public class CreateProductEndPoint : ICarterModule
   {
       public void AddRoutes(IEndpointRouteBuilder app) { }
   }
   ```

5. **Repository Implementation** (in `Catalog/Data/Repository/Products/`):
   ```csharp
   public interface IProductRepository { }
   public class ProductRepository : IProductRepository { }
   ```

6. **EF Core Configuration** (in `Catalog/Data/Configurations/`):
   ```csharp
   public class ProductConfiguration : IEntityTypeConfiguration<Product> { }
   ```

7. **Register in Module** (`CatalogModule.cs`):
   ```csharp
   services.AddScoped<IProductRepository, ProductRepository>();
   services.AddMediatR(CatalogRoot.AssemblyName);
   services.AddCarterWithAssemblies(CatalogRoot.AssemblyName);
   ```

### Environment Variables

Update `appsettings.json` or `appsettings.Development.json`:

```json
{
  "PostgresOptions": {
    "ConnectionString": "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "Serilog": {
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
    ]
  }
}
```

## Testing Instructions

### Current State

**Note**: This project does not currently have automated tests configured. When adding tests, follow these patterns:

### Recommended Test Structure

```
tests/
├── StarterKit.UnitTests/
│   ├── Modules/
│   │   └── Catalog/
│   │       ├── Domains/
│   │       │   └── Products/
│   │       │       ├── ProductTests.cs           # Domain model tests
│   │       │       └── CreateProductHandlerTests.cs
│   │       └── Data/
│   │           └── ProductRepositoryTests.cs
│   └── StarterKit.UnitTests.csproj
└── StarterKit.IntegrationTests/
    ├── Catalog/
    │   └── ProductEndpointTests.cs
    └── StarterKit.IntegrationTests.csproj
```

### Running Tests (When Implemented)

```powershell
# Run all tests
dotnet test

# Run tests in a specific project
dotnet test tests\StarterKit.UnitTests\StarterKit.UnitTests.csproj

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverageReporter=cobertura

# Run specific test category
dotnet test --filter "Category=Unit"
```

### Testing Patterns to Follow

1. **Unit Tests**: Test domain models, handlers, and business logic in isolation
2. **Integration Tests**: Use `WebApplicationFactory` to test endpoints with real database (test containers)
3. **Use xUnit**: Recommended test framework for .NET projects
4. **Use FluentAssertions**: For readable test assertions
5. **Use Moq or NSubstitute**: For mocking dependencies

## Code Style and Conventions

### EditorConfig Rules

This project uses strict EditorConfig rules (see `.editorconfig`):

- **Indent**: 4 spaces
- **Line endings**: CRLF (Windows)
- **Max line length**: 100 characters
- **var usage**: Required for built-in types and apparent types (warning level)
- **Naming**: PascalCase for types/methods, camelCase with underscore prefix for private fields
- **File-scoped namespaces**: Required (error level)
- **Using directives**: Outside namespace (warning level)

### C# Coding Standards

```csharp
// ✅ CORRECT: Factory method pattern for entity creation
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

// ✅ CORRECT: Primary constructor for handlers
public class CreateProductHandler(IProductRepository repository, IProfileRepository profileRepository)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        // Implementation
    }
}

// ✅ CORRECT: FluentValidation with clear rules
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(e => e.ArabicName).NotEmpty().WithMessage("Arabic Name is required");
        RuleFor(e => e.EnglishName).NotEmpty().WithMessage("English Name is required");
        RuleFor(e => e.Email).EmailAddress().When(e => !string.IsNullOrEmpty(e.Email));
    }
}

// ✅ CORRECT: Carter endpoint with versioning
public class CreateProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/Products", async (CreateProductRequest request, ISender sender) =>
            {
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

### File Organization

- **One class per file**: Use the class name as the file name
- **Folder structure matches namespace**: `Catalog/Domains/Products/Models/Product.cs` → `namespace Catalog.Domains.Products.Models`
- **GlobalUsings.cs**: Contains common global using directives for the project
- **Feature folders**: Group related files (Handler, Endpoint, Validator) in feature-specific folders

### Naming Conventions

- **Interfaces**: Prefix with `I` (e.g., `IProductRepository`)
- **Private fields**: Prefix with underscore (e.g., `_repository`, `_categoryAssignments`)
- **Async methods**: Suffix with `Async` (e.g., `GetByIdAsync`)
- **Constants**: PascalCase
- **DTOs**: Suffix with `Dto`, `Command`, `Query`, `Result`, `Request`, or `Response`
- **Handlers**: Suffix with `Handler` (e.g., `CreateProductHandler`)
- **Endpoints**: Suffix with `EndPoint` (e.g., `CreateProductEndPoint`)
- **Validators**: Suffix with `Validator` (e.g., `CreateProductCommandValidator`)

### Domain-Driven Design Patterns

1. **Aggregates**: Use `Aggregate<TId>` or `NamedAggregate<TId>` base classes
2. **Factory Methods**: Use static `Create()` methods for entity instantiation
3. **Encapsulation**: Private setters, expose collections as `IReadOnlyCollection<T>`
4. **Domain Events**: Raise events using `AddDomainEvent()` method
5. **Value Objects**: Immutable records for concepts without identity
6. **Repository per Aggregate**: One repository interface per aggregate root

## Build and Deployment

### Build Commands

```powershell
# Build entire solution
dotnet build

# Build specific project
dotnet build src\Api\Api.csproj

# Build in Release mode
dotnet build --configuration Release

# Clean build artifacts
dotnet clean
```

### Publish Commands

```powershell
# Publish API for deployment
cd src\Api
dotnet publish --configuration Release --output .\publish

# Publish with specific runtime
dotnet publish --configuration Release --runtime win-x64 --self-contained false
```

### Docker Support

The API project includes Docker support:

```powershell
# Build Docker image
docker build -t starterkit-api -f src/Api/Dockerfile .

# Run with Docker Compose (if configured)
docker-compose up -d
```

### Environment Configurations

- **Development**: Uses `appsettings.Development.json`, local PostgreSQL, Seq, Redis
- **Staging**: Configure `appsettings.Staging.json` with staging database connections
- **Production**: Configure `appsettings.Production.json` with production settings

## Pull Request Guidelines

### PR Title Format

```
[Module] Feature/Fix: Brief description

Examples:
[Catalog] Add GetProductById query endpoint
[Catalog] Fix product service place assignment validation
[Shared] Refactor pagination to support filtering
```

### Required Checks Before PR

```powershell
# 1. Build the solution
dotnet build

# 2. Run all tests (when implemented)
dotnet test

# 3. Check for formatting issues
dotnet format --verify-no-changes

# 4. Apply formatting (if needed)
dotnet format

# 5. Verify migrations can be applied
cd src\Api
dotnet ef database update --context CatalogDbContext
```

### PR Checklist

- [ ] Code follows project conventions and style guidelines
- [ ] New features include appropriate domain events
- [ ] Repository interfaces are created for new aggregates
- [ ] Carter endpoints include API versioning and documentation
- [ ] FluentValidation rules are added for new commands
- [ ] EF Core configurations are added for new entities
- [ ] Migrations are generated and tested
- [ ] Global usings are updated if new namespaces are heavily used
- [ ] No sensitive data (connection strings, secrets) in committed code

## Module Development Guide

### Creating a New Module

Use the **Catalog** module as a reference template:

1. **Create folder structure**:
   ```
   src/Modules/NewModule/
   ├── NewModule/
   │   ├── Domains/
   │   │   └── [Aggregates]/
   │   │       ├── Models/
   │   │       ├── Features/
   │   │       ├── Events/
   │   │       ├── Exceptions/
   │   │       └── Mappings/
   │   ├── Data/
   │   │   ├── Repository/
   │   │   ├── Configurations/
   │   │   ├── Migrations/
   │   │   ├── Seed/
   │   │   └── NewModuleDbContext.cs
   │   ├── NewModuleModule.cs
   │   ├── GlobalUsings.cs
   │   └── NewModule.csproj
   └── NewModule.Contracts/
       └── NewModule.Contracts.csproj
   ```

2. **Create module registration** (`NewModuleModule.cs`):
   ```csharp
   public static class NewModuleModule
   {
       public static IServiceCollection AddNewModuleModule(
           this IServiceCollection services,
           IConfiguration configuration)
       {
           // Register repositories
           services.AddScoped<IExampleRepository, ExampleRepository>();
           
           // Register MediatR, Mapster, Carter
           services.AddMediatR(NewModuleRoot.AssemblyName);
           services.AddCustomMapster(NewModuleRoot.AssemblyName);
           services.AddCarterWithAssemblies(NewModuleRoot.AssemblyName);
           
           // Register DbContext
           services.AddCustomDbContext<NewModuleDbContext>(configuration, "starterkit");
           services.AddScoped<IDataSeeder, NewModuleDataSeeder>();
           
           return services;
       }
       
       public static IApplicationBuilder UseNewModuleModule(this IApplicationBuilder app)
       {
           app.UseMigration<NewModuleDbContext>();
           return app;
       }
   }
   ```

3. **Register in API** (`Program.cs`):
   ```csharp
   builder.Services.AddMassTransitWithAssemblies(
       CatalogRoot.AssemblyName,
       CatalogRoot.AssemblyName,
       NewModuleRoot.AssemblyName);
   
   builder.Services.AddNewModuleModule(configuration);
   app.UseNewModuleModule();
   ```

4. **Create DbContext**:
   ```csharp
   public class NewModuleDbContext(DbContextOptions<NewModuleDbContext> options)
       : DbContext(options)
   {
       public DbSet<ExampleEntity> Examples => Set<ExampleEntity>();
       
       protected override void OnModelCreating(ModelBuilder builder)
       {
           builder.HasDefaultSchema("newmodule");
           builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
           base.OnModelCreating(builder);
       }
   }
   ```

## Additional Notes

### Common Patterns and Best Practices

1. **Pagination**: Use `PaginationRequest` and `PaginatedResult<T>` from `Shared.Core.Pagination`
2. **Response Wrapping**: Use `ApiResponse<T>` from `Shared.Core.Response`
3. **Exception Handling**: Create domain-specific exceptions inheriting from base exception types
4. **Logging**: Inject `ILogger<T>` and use structured logging with Serilog
5. **Async All The Way**: Use async/await for all I/O operations, always pass `CancellationToken`

### Database Migration Workflow

```powershell
# Always specify context and output directory for module migrations
cd src\Api

# Add migration for Catalog module
dotnet ef migrations add AddProductCategorys --context CatalogDbContext --output-dir ..\Modules\Catalog\Catalog\Data\Migrations

# View migration SQL without applying
dotnet ef migrations script --context CatalogDbContext

# Apply migrations
dotnet ef database update --context CatalogDbContext

# Rollback to specific migration
dotnet ef database update <PreviousMigrationName> --context CatalogDbContext
```

### Debugging Tips

1. **Seq Dashboard**: http://localhost:5341 - View structured logs in real-time
2. **Aspire Dashboard**: Check console output for URL - Monitor all services, metrics, traces
3. **Database Inspection**: Use pgAdmin or similar tool to connect to PostgreSQL
4. **Redis Inspection**: Use RedisInsight or redis-cli
5. **API Documentation**: Swagger at `/swagger` or Scalar at `/scalar/v1`

### Performance Considerations

- **Use AsNoTracking()**: For read-only queries to improve performance
- **Projection**: Select only needed columns using LINQ projections
- **Pagination**: Always paginate large result sets
- **Caching**: Use Redis for frequently accessed, rarely changing data
- **Async Operations**: Never block on async calls, use async/await properly
- **N+1 Queries**: Use `.Include()` to eager load related entities

### Security Best Practices

- **Never commit secrets**: Use User Secrets for local development, environment variables for deployed environments
- **Validate all inputs**: Use FluentValidation for all commands
- **Parameterized queries**: EF Core handles this automatically, avoid raw SQL
- **Connection string security**: Store in appsettings with appropriate permissions

### Troubleshooting

**Issue**: Migration fails with "relation already exists"
- **Solution**: Check if migration was partially applied, either rollback or manually fix database

**Issue**: Carter endpoints not discovered
- **Solution**: Ensure `AddCarterWithAssemblies()` includes the correct assembly and module is registered in `Program.cs`

**Issue**: MediatR handler not found
- **Solution**: Verify handler implements correct interface and `AddMediatR()` includes the assembly

**Issue**: Database connection fails
- **Solution**: Check PostgreSQL is running, verify connection string in appsettings.json, ensure database exists

**Issue**: Redis connection fails
- **Solution**: Verify Redis is running on specified port, check connection string

---

**Last Updated**: 2025-12-27  
**Project Version**: .NET 9.0  
**Target Framework**: net9.0  
**Architecture**: Modular Monolith with Vertical Slices and Clean Architecture
