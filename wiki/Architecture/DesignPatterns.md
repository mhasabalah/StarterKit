# Design Patterns

## Architectural Patterns

### 1. CQRS (Command Query Responsibility Segregation)

**Purpose**: Separate read and write operations

**Implementation**:
```csharp
// Command (Write)
public record CreateProductCommand(string ArabicName, string EnglishName) 
    : ICommand<Result<Guid>>;

// Query (Read)
public record GetProductByIdQuery(Guid Id) 
    : IQuery<Result<ProductDto>>;
```

**Benefits**:
- Optimized read/write models
- Clearer intent
- Independent scaling

### 2. Repository Pattern

**Purpose**: Abstract data access logic

**Implementation**:
```csharp
// Interface (Domain layer)
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken);
    void Add(Product product);
    void Update(Product product);
    void Delete(Product product);
}

// Implementation (Data layer)
public class ProductRepository : Repository<Product>, IProductRepository
{
    public ProductRepository(CatalogDbContext context) : base(context) { }
    
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Products
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }
}
```

**Benefits**:
- Testability
- Separation of concerns
- Swappable implementations

### 3. Unit of Work Pattern

**Purpose**: Transaction management across repositories

**Implementation**:
```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}

// DbContext implements Unit of Work
public class CatalogDbContext : DbContext, IUnitOfWork
{
    // SaveChangesAsync ensures atomic operations
}
```

### 4. Factory Pattern

**Purpose**: Encapsulate object creation logic

**Implementation**:
```csharp
public class Product : NamedAggregate
{
    // Private constructor
    private Product() { }
    
    // Factory method
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
}
```

**Benefits**:
- Controlled object creation
- Validation at creation
- Domain events on creation

### 5. Strategy Pattern

**Purpose**: Encapsulate algorithms and make them interchangeable

**Implementation**:
```csharp
public interface IProductApprovalStrategy
{
    Task<bool> CanApproveAsync(Product product);
    Task ApproveAsync(Product product);
}

public class StandardApprovalStrategy : IProductApprovalStrategy
{
    public async Task<bool> CanApproveAsync(Product product)
    {
        // Standard approval logic
    }
}

public class PremiumApprovalStrategy : IProductApprovalStrategy
{
    public async Task<bool> CanApproveAsync(Product product)
    {
        // Premium fast-track approval
    }
}
```

### 6. Mediator Pattern (via MediatR)

**Purpose**: Reduce coupling between components

**Implementation**:
```csharp
// Handler doesn't know about caller
public class CreateProductHandler : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProductCommand command, 
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}

// Caller doesn't know about handler
public class ProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/products", async (CreateProductCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
    }
}
```

### 7. Specification Pattern

**Purpose**: Encapsulate business rules for querying

**Implementation**:
```csharp
public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();
    
    public bool IsSatisfiedBy(T entity)
    {
        var predicate = ToExpression().Compile();
        return predicate(entity);
    }
}

public class ActiveProductsSpecification : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
    {
        return product => product.IsActive && !product.IsDeleted;
    }
}

// Usage in repository
var activeProducts = await _repository.FindAsync(new ActiveProductsSpecification());
```

### 8. Domain Event Pattern

**Purpose**: Decouple domain logic from side effects

**Implementation**:
```csharp
// Event
public record ProductCreatedEvent(Product Product) : IDomainEvent;

// Entity raises event
public class Product : NamedAggregate
{
    public static Product Create(...)
    {
        var product = new Product { ... };
        product.AddDomainEvent(new ProductCreatedEvent(product));
        return product;
    }
}

// Event handler
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    public async Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Send email, update cache, log, etc.
    }
}
```

### 9. Builder Pattern

**Purpose**: Construct complex objects step by step

**Implementation**:
```csharp
public class ProductFilterBuilder
{
    private readonly ProductFilter _filter = new();
    
    public ProductFilterBuilder WithPriceRange(decimal min, decimal max)
    {
        _filter.MinPrice = min;
        _filter.MaxPrice = max;
        return this;
    }
    
    public ProductFilterBuilder WithLocation(string city, string district)
    {
        _filter.City = city;
        _filter.District = district;
        return this;
    }
    
    public ProductFilter Build() => _filter;
}

// Usage
var filter = new ProductFilterBuilder()
    .WithPriceRange(100000, 500000)
    .WithLocation("Riyadh", "Al Malqa")
    .Build();
```

### 10. Decorator Pattern (via MediatR Behaviors)

**Purpose**: Add behavior to existing functionality

**Implementation**:
```csharp
public class ValidationBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Pre-processing (validation)
        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            
        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();
            
        if (failures.Count != 0)
            throw new ValidationException(failures);
        
        // Call next in pipeline
        return await next();
    }
}
```

### 11. Chain of Responsibility (MediatR Pipeline)

**Purpose**: Pass request through chain of handlers

**Pipeline Order**:
```csharp
Request → Logging → Validation → Transaction → Handler → Response
```

**Implementation**:
```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
});
```

## Best Practices

### When to Use Each Pattern

| Pattern | Use Case |
|---------|----------|
| CQRS | Separate read/write models |
| Repository | Abstract data access |
| Unit of Work | Transaction management |
| Factory | Complex object creation |
| Strategy | Interchangeable algorithms |
| Mediator | Decouple request/handler |
| Specification | Reusable query logic |
| Domain Event | Decouple side effects |
| Builder | Step-by-step construction |
| Decorator | Add cross-cutting concerns |

### Anti-Patterns to Avoid

❌ **Anemic Domain Model**: Entities with no behavior
✅ **Rich Domain Model**: Entities with business logic

❌ **Service Layer God Classes**: One service does everything
✅ **Vertical Slices**: Feature-focused handlers

❌ **Repository Leakage**: IQueryable in interfaces
✅ **Encapsulated Queries**: Specific methods

## Next Steps
- [Coding Standards](CodingStandards.md)
- [Database Conventions](DatabaseConventions.md)
- [API Guidelines](ApiGuidelines.md)
