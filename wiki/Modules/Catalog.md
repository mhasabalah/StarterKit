# Catalog Module (Sample) and Adding a New Module

`Catalog` is a deliberately small **sample module** with a single aggregate, `Product`. It exists to show the
StarterKit vertical-slice layout end to end. Copy it when you add a module, and delete it when your project no
longer needs it.

## What it contains

```
src/Modules/Catalog/
├── Catalog.Contracts/                  # Public surface other modules may reference
│   ├── GlobalUsings.cs
│   └── Products/
│       ├── Commands/                   # CreateProductCommand, UpdateProductCommand, DeleteProductCommand
│       ├── Queries/                    # GetProductsQuery, GetProductByIdQuery
│       └── Dtos/                       # ProductDto
├── Catalog/                            # Implementation (internal to the module)
│   ├── CatalogModule.cs                # AddCatalogModule / UseCatalogModule
│   ├── GlobalUsings.cs                 # global usings + CatalogRoot (assembly marker)
│   ├── Data/
│   │   ├── CatalogDbContext.cs         # schema "catalog", snake_case, version concurrency tokens
│   │   ├── Configurations/             # ProductConfiguration (IEntityTypeConfiguration)
│   │   ├── Repository/Products/        # IProductRepository / ProductRepository
│   │   └── Migrations/
│   └── Domains/Products/
│       ├── Models/                     # Product entity: factory method, private setters, domain events
│       ├── Features/
│       │   ├── CreateProduct/          # CreateProductHandler.cs (validator + handler) + CreateProductEndPoint.cs
│       │   ├── UpdateProduct/
│       │   ├── DeleteProduct/
│       │   ├── GetProducts/            # query handler projects from CatalogDbContext, returns PaginatedResult<T>
│       │   └── GetProductById/
│       ├── Events/  EventHandlers/  Exceptions/  Mappings/  Services/
└── Catalog.Tests/                      # Unit tests for the module
```

Key points the sample demonstrates:

- **Command handlers** use the repository; the `*CommandValidator` lives in the same file, above the handler.
- **Query handlers** use `CatalogDbContext` directly for LINQ projection and pagination.
- **Endpoints** are Carter modules (`ICarterModule`), one per feature folder.
- **Optimistic concurrency**: `UpdateProductCommand` carries an optional `long? Version`; `ProductDto` exposes `Version`.
- **Cross-module use** (attachments/images) goes through `Attachment.Contracts` + `ISender`, never through another module's models.

## Checklist: adding a new module

Replace `Orders` / `Order` with your module and aggregate names.

1. **Projects**
   - `src/Modules/Orders/Orders.Contracts/Orders.Contracts.csproj`
   - `src/Modules/Orders/Orders/Orders.csproj`
   - `src/Modules/Orders/Orders.Tests/Orders.Tests.csproj`
   - Package versions come from `Directory.Packages.props` - add `<PackageReference Include="..." />` without versions.
2. **Project references**
   - `Orders.Contracts` -> `Shared.Contracts`
   - `Orders` -> `Shared.Core`, `Orders.Contracts` (+ other modules' `*.Contracts` only, never their implementation projects)
   - `Orders.Tests` -> `Orders`; add `<InternalsVisibleTo Include="Orders.Tests" />` to `Orders.csproj`
   - `src/Api/Api.csproj` -> `Orders`
3. **GlobalUsings.cs** in each project; in the implementation project add the assembly marker:
   ```csharp
   public static class OrdersRoot
   {
       public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
   }
   ```
4. **DbContext** (`Data/OrdersDbContext.cs`), inheriting `AppDbContextBase`:
   ```csharp
   builder.HasDefaultSchema("orders");
   builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
   builder.ToSnakeCaseTables();
   builder.ConfigureVersionConcurrencyTokens();
   ```
   Do not call `FilterSoftDeletedProperties()` unless the aggregate really needs soft delete.
5. **Module class** (`OrdersModule.cs`):
   ```csharp
   public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
   {
       services.AddScoped<IOrderRepository, OrderRepository>();
       services.AddMediatR(OrdersRoot.AssemblyName);
       services.AddCustomMapster(OrdersRoot.AssemblyName);
       services.AddCarterWithAssemblies(OrdersRoot.AssemblyName);
       services.AddCustomDbContext<OrdersDbContext>(configuration, "starterkit");
       return services;
   }

   public static IApplicationBuilder UseOrdersModule(this IApplicationBuilder app)
   {
       app.UseMigration<OrdersDbContext>();
       return app;
   }
   ```
6. **Program.cs registration** (`src/Api/Program.cs`):
   - add `OrdersRoot.AssemblyName` to `AddMassTransitWithAssemblies(...)` (consumers/events)
   - add the assembly that defines the module's enums to `AddEnumLookup(...)`
   - call `builder.Services.AddOrdersModule(configuration);` after the existing modules
   - call `app.UseOrdersModule();` with the other `Use<Module>Module()` calls
7. **Solution**: add the three projects to `StarterKit.slnx` under a `/src/Modules/Orders/` folder.
8. **First migration** (from `src/Api`):
   ```bash
   dotnet ef migrations add InitialOrders \
     --context OrdersDbContext \
     --output-dir ../Modules/Orders/Orders/Data/Migrations
   ```
9. **Tests**: validators, entity factory methods/invariants, handlers (happy path + not found + conflict),
   and an integration/e2e test for each endpoint.
10. **Docs**: add a short page under `wiki/Modules/` and link it from `wiki/INDEX.md`.
11. **Build**: `dotnet build StarterKit.slnx` and `dotnet test StarterKit.slnx` must pass.

## Removing the Catalog sample

1. Remove `AddCatalogModule` / `UseCatalogModule` (and any Catalog assembly references) from `src/Api/Program.cs`.
2. Remove the `Catalog` project reference from `src/Api/Api.csproj`.
3. Remove the `/src/Modules/Catalog/` folder entries from `StarterKit.slnx` and delete `src/Modules/Catalog/`.
4. Drop the `catalog` schema from existing databases (`DROP SCHEMA catalog CASCADE;`).
5. Run `dotnet build StarterKit.slnx`.
