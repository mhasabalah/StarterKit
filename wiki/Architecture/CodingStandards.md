# Coding Standards

## C# Language Features

### File-Scoped Namespaces
✅ **Use**:
```csharp
namespace Catalog.Domains.Products.Models;

public class Product : NamedAggregate
{
    // Implementation
}
```

❌ **Avoid**:
```csharp
namespace Catalog.Domains.Products.Models
{
    public class Product : NamedAggregate
    {
        // Implementation
    }
}
```

### Primary Constructors
✅ **Use for handlers and services**:
```csharp
public class CreateProductHandler(
    IProductRepository repository, 
    IProfileRepository profileRepository)
    : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        // Use repository and profileRepository directly
    }
}
```

### Record Types
✅ **Use for DTOs, Commands, and Queries**:
```csharp
public record CreateProductCommand(
    string ArabicName,
    string EnglishName,
    Guid ProfileId
) : ICommand<Result<Guid>>;

public record ProductDto(
    Guid Id,
    string ArabicName,
    string EnglishName
);
```

### Init-Only Properties
✅ **Use for immutable data**:
```csharp
public class ProductDto
{
    public Guid Id { get; init; }
    public string ArabicName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
}
```

## Naming Conventions

### Classes and Interfaces
```csharp
// Pascal case
public class Product { }
public interface IProductRepository { }
public record CreateProductCommand { }
public class ProductDto { }
```

### Methods
```csharp
// Pascal case, descriptive verbs
public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
public void Delete(Product product)
public static Product Create(Guid id, string name)
```

### Variables and Parameters
```csharp
// Camel case
var productId = Guid.NewGuid();
string arabicName = command.ArabicName;
CancellationToken cancellationToken = default;
```

### Private Fields
```csharp
// Underscore prefix, camel case
private readonly IProductRepository _repository;
private readonly List<ProductCategory> _categoryAssignments = new();
```

### Constants
```csharp
// Pascal case
public const string DefaultCulture = "ar-SA";
public const int MaxPageSize = 100;
```

## Entity Design

### Rich Domain Models
✅ **Encapsulate business logic**:
```csharp
public class Product : NamedAggregate
{
    private readonly List<ProductCategory> _categoryAssignments = new();
    public IReadOnlyCollection<ProductCategory> CategoryAssignments => 
        _categoryAssignments.AsReadOnly();
    
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
    
    // Business logic methods
    public void Update(string arabicName, string englishName)
    {
        ArgumentException.ThrowIfNullOrEmpty(arabicName, nameof(arabicName));
        ArgumentException.ThrowIfNullOrEmpty(englishName, nameof(englishName));
        
        ArabicName = arabicName;
        EnglishName = englishName;
        
        AddDomainEvent(new ProductUpdatedEvent(this));
    }
    
    public void AssignToServicePlace(Guid categoryId, bool isActive = true)
    {
        if (!_categoryAssignments.Any(asp => asp.CategoryId == categoryId))
        {
            var assignment = ProductCategory.Create(Id, categoryId, isActive);
            _categoryAssignments.Add(assignment);
        }
    }
}
```

### Private Collections
```csharp
// Private backing field (mutable)
private readonly List<ProductCategory> _categoryAssignments = new();

// Public read-only property
public IReadOnlyCollection<ProductCategory> CategoryAssignments => 
    _categoryAssignments.AsReadOnly();
```

### Domain Events
```csharp
// Raise events on state changes
product.AddDomainEvent(new ProductCreatedEvent(product));
product.AddDomainEvent(new ProductUpdatedEvent(product));
```

## Handler Design

### Structure
```csharp
// Validator first
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(e => e.ArabicName)
            .NotEmpty().WithMessage("Arabic Name is required");
        RuleFor(e => e.EnglishName)
            .NotEmpty().WithMessage("English Name is required");
    }
}

// Handler with primary constructor
public class CreateProductHandler(
    IProductRepository repository,
    IProfileRepository profileRepository)
    : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProductCommand command, 
        CancellationToken cancellationToken)
    {
        // 1. Validation (if not handled by pipeline)
        
        // 2. Business logic
        var profile = await profileRepository.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<Guid>(ProfileErrors.NotFound(command.ProfileId));
        
        // 3. Create entity
        var product = Product.Create(
            Guid.NewGuid(),
            command.ArabicName,
            command.EnglishName,
            command.ProfileId
        );
        
        // 4. Persist
        repository.Add(product);
        await repository.SaveChangesAsync(cancellationToken);
        
        // 5. Return result
        return Result.Success(product.Id);
    }
}
```

### Error Handling
✅ **Use Result pattern**:
```csharp
public record Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }
    
    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(Error error) => new() { IsSuccess = false, Error = error };
}

// Usage
if (product is null)
    return Result.Failure<ProductDto>(ProductErrors.NotFound(id));

return Result.Success(productDto);
```

## Endpoint Design

### Carter Endpoints
```csharp
public class ProductEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();
        
        // POST /api/products
        group.MapPost("/", CreateProduct)
            .WithName("CreateProduct")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        
        // GET /api/products/{id}
        group.MapGet("/{id:guid}", GetProductById)
            .WithName("GetProductById")
            .Produces<ProductDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
    
    private async Task<IResult> CreateProduct(
        CreateProductCommand command,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        
        return result.IsSuccess
            ? Results.Created($"/api/products/{result.Value}", result.Value)
            : Results.BadRequest(result.Error);
    }
    
    private async Task<IResult> GetProductById(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var query = new GetProductByIdQuery(id);
        var result = await sender.Send(query, ct);
        
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.NotFound(result.Error);
    }
}
```

## Validation Rules

### FluentValidation
```csharp
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // Required fields
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");
        
        // Email validation
        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("Invalid email format");
        
        // Conditional validation
        RuleFor(x => x.Price)
            .GreaterThan(0).When(x => x.ListingType == ListingType.Sale)
            .WithMessage("Price must be greater than 0 for sale listings");
        
        // Custom validation
        RuleFor(x => x.StartDate)
            .Must(BeValidDate).WithMessage("Start date must be in the future");
        
        // Nested validation
        RuleFor(x => x.Address)
            .SetValidator(new AddressValidator());
    }
    
    private bool BeValidDate(DateTime date)
    {
        return date > DateTime.UtcNow;
    }
}
```

## Async/Await Guidelines

### Always use CancellationToken
```csharp
✅ public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
❌ public async Task<Product?> GetByIdAsync(Guid id)
```

### Avoid async void
```csharp
✅ public async Task DoWorkAsync() { }
❌ public async void DoWorkAsync() { }
```

### ConfigureAwait
```csharp
// In library code (not needed in ASP.NET Core)
var result = await SomeMethodAsync().ConfigureAwait(false);
```

## Exception Handling

### Domain Exceptions
```csharp
public class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(Guid id) 
        : base($"Product with ID '{id}' was not found")
    {
    }
}
```

### Guard Clauses
```csharp
ArgumentException.ThrowIfNullOrEmpty(arabicName, nameof(arabicName));
ArgumentNullException.ThrowIfNull(profile, nameof(profile));
```

## Comments and Documentation

### XML Documentation
```csharp
/// <summary>
/// Creates a new product with the specified details
/// </summary>
/// <param name="id">Unique identifier for the product</param>
/// <param name="arabicName">Arabic name of the product</param>
/// <param name="englishName">English name of the product</param>
/// <param name="profileId">Associated profile ID</param>
/// <returns>A new product instance with domain event raised</returns>
public static Product Create(Guid id, string arabicName, string englishName, Guid profileId)
```

### Code Comments
```csharp
// Only comment when necessary
// ✅ Good: Explains WHY
// Disable validation for system administrators as per business rule #123
if (user.IsSystemAdmin)
    bypassValidation = true;

// ❌ Bad: Explains WHAT (obvious from code)
// Set the name to the provided name
this.Name = name;
```

## Global Usings

### Per Module
```csharp
// GlobalUsings.cs
global using StarterKit.Shared;
global using StarterKit.Shared.Contracts;
global using Catalog.Data;
global using Catalog.Domains;
global using MediatR;
global using FluentValidation;
global using Mapster;
global using Carter;
```

## Next Steps
- [Database Conventions](DatabaseConventions.md)
- [API Guidelines](ApiGuidelines.md)
