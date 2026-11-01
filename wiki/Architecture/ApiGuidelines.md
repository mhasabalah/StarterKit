# API Guidelines

## Endpoint Structure

### RESTful Conventions

```
GET    /api/{module}/{resource}              # List all
GET    /api/{module}/{resource}/{id}         # Get by ID
POST   /api/{module}/{resource}              # Create
PUT    /api/{module}/{resource}/{id}         # Update
DELETE /api/{module}/{resource}/{id}         # Delete
PATCH  /api/{module}/{resource}/{id}         # Partial update
```

### Examples
```
GET    /api/catalog/products
GET    /api/catalog/products/123e4567-e89b-12d3-a456-426614174000
POST   /api/catalog/products
PUT    /api/catalog/products/123e4567-e89b-12d3-a456-426614174000
DELETE /api/catalog/products/123e4567-e89b-12d3-a456-426614174000
```

## Carter Endpoint Implementation

### Basic CRUD Endpoints
```csharp
public class ProductEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog/products")
            .WithTags("Products")
            .RequireAuthorization()
            .WithOpenApi();
        
        group.MapGet("/", GetAllProducts)
            .WithName("GetAllProducts")
            .Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK);
        
        group.MapGet("/{id:guid}", GetProductById)
            .WithName("GetProductById")
            .Produces<ProductDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        
        group.MapPost("/", CreateProduct)
            .WithName("CreateProduct")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        
        group.MapPut("/{id:guid}", UpdateProduct)
            .WithName("UpdateProduct")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        
        group.MapDelete("/{id:guid}", DeleteProduct)
            .WithName("DeleteProduct")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
```

### Query Parameters
```csharp
// Pagination
group.MapGet("/", async (
    [AsParameters] PaginationQuery pagination,
    ISender sender,
    CancellationToken ct) =>
{
    var query = new GetProductsQuery(pagination.PageNumber, pagination.PageSize);
    var result = await sender.Send(query, ct);
    return Results.Ok(result);
});

public record PaginationQuery(
    int PageNumber = 1,
    int PageSize = 10
);

// Filtering
group.MapGet("/search", async (
    string? name,
    bool? isActive,
    ISender sender,
    CancellationToken ct) =>
{
    var query = new SearchProductsQuery(name, isActive);
    var result = await sender.Send(query, ct);
    return Results.Ok(result);
});
```

## Response Patterns

### Success Responses

#### 200 OK - Resource Found
```csharp
private async Task<IResult> GetProductById(
    Guid id,
    ISender sender,
    CancellationToken ct)
{
    var result = await sender.Send(new GetProductByIdQuery(id), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.NotFound(result.Error);
}
```

#### 201 Created - Resource Created
```csharp
private async Task<IResult> CreateProduct(
    CreateProductCommand command,
    ISender sender,
    CancellationToken ct)
{
    var result = await sender.Send(command, ct);
    return result.IsSuccess
        ? Results.Created($"/api/catalog/products/{result.Value}", result.Value)
        : Results.BadRequest(result.Error);
}
```

#### 204 No Content - Success Without Body
```csharp
private async Task<IResult> UpdateProduct(
    Guid id,
    UpdateProductCommand command,
    ISender sender,
    CancellationToken ct)
{
    var result = await sender.Send(command with { Id = id }, ct);
    return result.IsSuccess
        ? Results.NoContent()
        : Results.BadRequest(result.Error);
}
```

### Error Responses

#### 400 Bad Request - Validation Error
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "ArabicName": ["Arabic Name is required"],
    "Email": ["Invalid email format"]
  }
}
```

#### 404 Not Found - Resource Not Found
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Product with ID '123e4567-e89b-12d3-a456-426614174000' was not found"
}
```

#### 409 Conflict - Business Rule Violation
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "An product with this email already exists"
}
```

#### 500 Internal Server Error - Unexpected Error
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "traceId": "00-12345-67890-00"
}
```

## Request/Response DTOs

### Command DTOs
```csharp
public record CreateProductCommand(
    string ArabicName,
    string EnglishName,
    string UserName,
    string? Email,
    string? PhoneNumber,
    Guid ProfileId
) : ICommand<Result<Guid>>;

public record UpdateProductCommand(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? Email,
    string? PhoneNumber
) : ICommand<Result>;
```

### Query DTOs
```csharp
public record GetProductsQuery(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<ProductDto>>>;

public record GetProductByIdQuery(Guid Id) : IQuery<Result<ProductDto>>;
```

### Response DTOs
```csharp
public record ProductDto
{
    public Guid Id { get; init; }
    public string ArabicName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public ProfileDto? Profile { get; init; }
}

public record PagedResult<T>
{
    public List<T> Items { get; init; } = new();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

## Versioning

### URL Versioning
```csharp
var v1 = app.MapGroup("/api/v1/products").WithTags("Products V1");
var v2 = app.MapGroup("/api/v2/products").WithTags("Products V2");

v1.MapGet("/", GetProductsV1);
v2.MapGet("/", GetProductsV2);
```

### Header Versioning
```csharp
app.MapGet("/api/products", async (
    HttpContext context,
    ISender sender,
    CancellationToken ct) =>
{
    var version = context.Request.Headers["api-version"].FirstOrDefault() ?? "1.0";
    
    return version switch
    {
        "1.0" => await GetProductsV1(sender, ct),
        "2.0" => await GetProductsV2(sender, ct),
        _ => Results.BadRequest("Unsupported API version")
    };
});
```

## Authentication & Authorization

### Require Authentication
```csharp
var group = app.MapGroup("/api/catalog/products")
    .RequireAuthorization(); // All endpoints require auth
```

### Role-Based Authorization
```csharp
group.MapPost("/", CreateProduct)
    .RequireAuthorization(policy => policy.RequireRole("Admin", "Manager"));

group.MapDelete("/{id:guid}", DeleteProduct)
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
```

### Policy-Based Authorization
```csharp
group.MapPut("/{id:guid}", UpdateProduct)
    .RequireAuthorization("ProductManagement");

// Policy definition (in Program.cs)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ProductManagement", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Admin") ||
            context.User.IsInRole("Manager")));
});
```

### Custom Authorization
```csharp
group.MapGet("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal user,
    ISender sender,
    CancellationToken ct) =>
{
    // Check if user owns the resource
    var userId = Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    var query = new GetProductByIdQuery(id);
    var result = await sender.Send(query, ct);
    
    if (result.IsSuccess && result.Value.UserId != userId && !user.IsInRole("Admin"))
        return Results.Forbid();
    
    return Results.Ok(result.Value);
});
```

## Rate Limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", options =>
    {
        options.PermitLimit = 10;
        options.Window = TimeSpan.FromSeconds(10);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 2;
    });
});

// Apply to endpoints
group.MapPost("/", CreateProduct)
    .RequireRateLimiting("fixed");
```

## CORS

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://example.com")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

app.UseCors();
```

## Request Validation

### FluentValidation Integration
```csharp
// Automatic validation via MediatR behavior
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
        if (!_validators.Any())
            return await next();
        
        var context = new ValidationContext<TRequest>(request);
        
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        
        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();
        
        if (failures.Count != 0)
            throw new ValidationException(failures);
        
        return await next();
    }
}
```

## OpenAPI/Swagger

### Configuration
```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StarterKit API",
        Version = "v1",
        Description = "StarterKit modular monolith API"
    });
    
    // Add JWT authentication
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });
    
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

app.UseSwagger();
app.UseSwaggerUI();
```

## Best Practices

### ✅ Do's
- Use Result pattern for error handling
- Validate all inputs with FluentValidation
- Use pagination for list endpoints
- Return appropriate HTTP status codes
- Use CancellationToken for all async operations
- Log all requests and responses
- Use OpenAPI/Swagger documentation
- Implement rate limiting for public APIs
- Use versioning for breaking changes

### ❌ Don'ts
- Don't expose internal exceptions to clients
- Don't return sensitive data in error messages
- Don't use exceptions for flow control
- Don't ignore validation errors
- Don't return entire entities from database
- Don't use generic object returns
- Don't skip authorization checks

## Next Steps
- [Testing Strategies](../Testing/TestingStrategies.md)
- [Module: Identity](../Identity/README.md)
