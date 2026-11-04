# add-feature templates

These are trimmed from `src/Modules/Catalog/Catalog/Domains/Products/Features/{CreateProduct,UpdateProduct,GetProducts}`.

## Contracts (`<M>.Contracts/<X>s/Commands/Create<X>Command.cs`)

```csharp
namespace {{Module}}.Contracts.{{Aggregate}}s.Commands;

public record Create{{Aggregate}}Command(LocalizedText Name, string Code, string? Description) : ICommand<Create{{Aggregate}}Result>;
public record Create{{Aggregate}}Result(Guid Id);

public record Update{{Aggregate}}Command(Guid Id, LocalizedText Name, string Code, string? Description, long? Version = null)
    : ICommand<Update{{Aggregate}}Result>;
public record Update{{Aggregate}}Result(bool Success, long Version);

public record Delete{{Aggregate}}Command(Guid Id) : ICommand<Delete{{Aggregate}}Result>;
public record Delete{{Aggregate}}Result(bool Success);
```

## Query and DTO

```csharp
using {{Module}}.Contracts.{{Aggregate}}s.Dtos;

namespace {{Module}}.Contracts.{{Aggregate}}s.Queries;

public record Get{{Aggregate}}sQuery : BaseQuery, IQuery<PaginatedResult<{{Aggregate}}Dto>>
{
    public string? Search { get; init; }
}

public record Get{{Aggregate}}ByIdQuery(Guid Id) : IQuery<{{Aggregate}}Dto>;
```

```csharp
using {{Module}}.Contracts.{{Aggregate}}s.Enums;

namespace {{Module}}.Contracts.{{Aggregate}}s.Dtos;

public record {{Aggregate}}Dto(
    Guid Id, LocalizedText Name, string Code, string? Description, {{Aggregate}}Status Status, DateTime? CreatedAt, long Version);
```

## Create: `Features/Create{{Aggregate}}/Create{{Aggregate}}Handler.cs`

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Features.Create{{Aggregate}};

public class Create{{Aggregate}}CommandValidator : AbstractValidator<Create{{Aggregate}}Command>
{
    public Create{{Aggregate}}CommandValidator(IStringLocalizer<{{Module}}Resource> localizer)
    {
        RuleFor(x => x.Name).NotNull().WithMessage(_ => localizer["Validation.Name.Required"]);
        RuleFor(x => x.Name.En).NotEmpty().MaximumLength({{Aggregate}}.NameMaxLength)
            .WithMessage(_ => localizer["Validation.Name.En.Required"]).When(x => x.Name is not null);
        RuleFor(x => x.Code).NotEmpty().MaximumLength({{Aggregate}}.CodeMaxLength)
            .WithMessage(_ => localizer["Validation.Code.Required"]);
        RuleFor(x => x.Description).MaximumLength({{Aggregate}}.DescriptionMaxLength)
            .WithMessage(_ => localizer["Validation.Description.MaxLength"]);
    }
}

public class Create{{Aggregate}}Handler(I{{Aggregate}}Repository repository)
    : ICommandHandler<Create{{Aggregate}}Command, Create{{Aggregate}}Result>
{
    public async Task<Create{{Aggregate}}Result> Handle(Create{{Aggregate}}Command command, CancellationToken cancellationToken)
    {
        var entity = {{Aggregate}}.Create(command.Name, command.Code, command.Description);   // invariants + domain event inside
        await repository.AddAsync(entity, cancellationToken);                    // saves (events dispatched after commit)
        return new Create{{Aggregate}}Result(entity.Id);
    }
}
```

## Create: `Features/Create{{Aggregate}}/Create{{Aggregate}}EndPoint.cs`

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Features.Create{{Aggregate}};

public record Create{{Aggregate}}Request(LocalizedText Name, string Code, string? Description);
public record Create{{Aggregate}}Response(Guid Id);

public class Create{{Aggregate}}EndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/{{aggregate}}s",
                async (Create{{Aggregate}}Request request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new Create{{Aggregate}}Command(request.Name, request.Code, request.Description), cancellationToken);
                    var response = new Create{{Aggregate}}Response(result.Id);
                    return Results.Created($"api/v1/{{aggregate}}s/{response.Id}", ApiResponseExtensions.Success(response));
                })
            .WithName("Create{{Aggregate}}")
            .WithTags("{{Aggregate}}s")
            .WithApiVersionSet(app.NewApiVersionSet("{{Module}}").Build())
            .Produces<ApiResponse<Create{{Aggregate}}Response>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Create {{Aggregate}}")
            .WithDescription("Creates a {{aggregate}}.")
            .RequireAuthorization()            // never leave auth implicit - see SKILL.md
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
```

## Update with optimistic concurrency: `Features/Update{{Aggregate}}/Update{{Aggregate}}Handler.cs`

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Features.Update{{Aggregate}};

public class Update{{Aggregate}}CommandValidator : AbstractValidator<Update{{Aggregate}}Command>
{
    public Update{{Aggregate}}CommandValidator(IStringLocalizer<{{Module}}Resource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

public class Update{{Aggregate}}Handler(I{{Aggregate}}Repository repository, {{Module}}DbContext dbContext)
    : ICommandHandler<Update{{Aggregate}}Command, Update{{Aggregate}}Result>
{
    public async Task<Update{{Aggregate}}Result> Handle(Update{{Aggregate}}Command command, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(command.Id, cancellationToken)
                     ?? throw new {{Aggregate}}NotFoundException(command.Id);

        dbContext.ExpectVersion(entity, command.Version);   // null = keep loaded version; stale → 409
        entity.Update(command.Name, command.Code, command.Description);   // domain method; never touch Version

        await repository.SaveChangesAsync(cancellationToken);
        return new Update{{Aggregate}}Result(true, entity.Version);
    }
}
```

The endpoint is `MapPut(EndpointConfig.BaseApiPath + "/{{aggregate}}s/{id:guid}", async ([FromRoute] Guid id,
[FromBody] Update{{Aggregate}}Request request, ISender sender, CancellationToken ct) => ...)`. The request record
ends with `long? Version = null`, and the route `id` is the source of truth. It produces 200/400/401/403/404/409.

## Delete

The handler loads the entity, calls a domain method (for example `entity.Delete()`, which raises an event), then
`repository.DeleteAsync` or soft delete, then saves. It takes no version field. The endpoint is
`MapDelete(".../{id:guid}")` and returns `Results.Ok(ApiResponseExtensions.Success(result))`. Add
`.DisallowImpersonation()` if the delete is destructive or owner-only.

## Paginated query: `Features/Get{{Aggregate}}s/Get{{Aggregate}}sHandler.cs`

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Features.Get{{Aggregate}}s;

public class Get{{Aggregate}}sQueryValidator : AbstractValidator<Get{{Aggregate}}sQuery>
{
    public Get{{Aggregate}}sQueryValidator(IStringLocalizer<{{Module}}Resource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
    }
}

public class Get{{Aggregate}}sHandler({{Module}}DbContext dbContext)
    : IQueryHandler<Get{{Aggregate}}sQuery, PaginatedResult<{{Aggregate}}Dto>>
{
    public async Task<PaginatedResult<{{Aggregate}}Dto>> Handle(Get{{Aggregate}}sQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.{{Aggregate}}s.AsNoTracking();   // tenant/branch/soft-delete filters apply automatically

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";             // escape user input like Catalog's LikePattern
            source = source.Where(x => EF.Functions.ILike(x.Name.En, pattern) || EF.Functions.ILike(x.Name.Ar, pattern));
        }

        return await source
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new {{Aggregate}}Dto(
                x.Id, new LocalizedText(x.Name.En, x.Name.Ar), x.Code, x.Description, x.Status, x.CreatedAt, x.Version))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
```

## Query endpoint

```csharp
app.MapGet(EndpointConfig.BaseApiPath + "/{{aggregate}}s",
        async ([AsParameters] Get{{Aggregate}}sQuery query, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, cancellationToken))))
    .WithName("Get{{Aggregate}}s")
    .WithTags("{{Aggregate}}s")
    .WithApiVersionSet(app.NewApiVersionSet("{{Module}}").Build())
    .Produces<ApiResponse<PaginatedResult<{{Aggregate}}Dto>>>(StatusCodes.Status200OK)
    .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
    .WithSummary("List {{Aggregate}}s")
    .RequireAuthorization()                // or "PageGate" for a public catalogue
    .RequireRateLimiting("search")
    .HasApiVersion(1.0);
```

## Localization keys

Add each key the validators use (`Validation.Name.Required`, `Validation.Name.En.Required`, `Validation.Code.Required`,
`Validation.Description.MaxLength`, `Validation.Id.Required`, `Validation.Version.Min`, `Validation.PageNumber.Min`,
`Validation.PageSize.Range`) to both resx files, `<M>Resource.resx` (en) and `<M>Resource.ar.resx` (ar):

```xml
<data name="Validation.Name.En.Required" xml:space="preserve"><value>English name is required.</value></data>
```
