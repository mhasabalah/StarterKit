# Coding standards

This is the house style for C# in StarterKit. The architecture rules (boundaries, tenancy, messaging) are in
`CLAUDE.md`. Copy-ready templates are in the `tools/claude/starterkit-plugin/skills/*/references/templates.md` files.
The reference implementation is `src/Modules/Catalog`.

## Language and project settings

- .NET 10 / C# latest, with `Nullable` and `ImplicitUsings` enabled (`Directory.Build.props`). Treat nullable
  warnings as bugs.
- File-scoped namespaces that match the folder (`Catalog.Domains.Products.Features.CreateProduct`).
- Package versions live only in `Directory.Packages.props`.
- Each module has a `GlobalUsings.cs` for its common namespaces. Don't repeat those `using`s in files.

## Types

| Use | For |
|---|---|
| `record` (positional) | Commands, queries, results, requests, responses, DTOs, domain events |
| `sealed class` | Services, consumers, jobs, options, integration events (with `init` properties) |
| Primary constructors | Handlers, services, repositories, endpoints' dependencies |
| `private set` + factory method | Entities and aggregates (`static Create(...)`, private parameterless ctor for EF) |
| `const` | Max lengths, index names (`ProductConfiguration.SkuIndexName`), job ids, permission values |

```csharp
public record CreateProductCommand(LocalizedText Name, string? Description, string Sku /*...*/)
    : ICommand<CreateProductResult>;

public class CreateProductHandler(IProductRepository repository, ILogger<CreateProductHandler> logger)
    : ICommandHandler<CreateProductCommand, CreateProductResult> { /* ... */ }
```

## Error handling

- **No Result type.** Code expresses failures by throwing typed exceptions: `NotFoundException` (404),
  `BadRequestException` / `DomainException` (400), `ForbiddenException` (403), `ConflictException` (409),
  `GoneException` (410), or a module subclass such as `ProductNotFoundException`.
- `GlobalExceptionHandler` + `ExceptionStatusMapper` (`Shared.Core/Exceptions/Handler`) are the only place that turns
  an exception into an HTTP response, in the `ApiResponse` error envelope. Don't build error responses in endpoints.
- Validation lives in FluentValidation validators (`ValidationBehavior`). Domain invariants live in entities and
  throw.
- Don't swallow exceptions. A best-effort side effect (cache invalidation, an optional cross-module call) catches,
  logs a warning with context, and continues, and says so in a comment.
- `catch (Exception ex) when (ex is not OperationCanceledException)` is the pattern for best-effort handlers.

## Async and cancellation

- Every I/O method is `async` and takes a `CancellationToken cancellationToken`; pass it all the way down.
- No `.Result`, `.Wait()` or `async void`.
- Hangfire jobs take `IJobCancellationToken?`.

## Time, ids and money

- Inject `TimeProvider` in new handlers, services and jobs. `DateTime.UtcNow` stays acceptable inside entity
  methods.
- All timestamps are UTC `DateTime`.
- Ids are `Guid` from `Guid.NewGuid()` in factories. Seed data uses fixed ids (`TenantSeedIds.For` per tenant).
- Money is `decimal` with an explicit currency (see `Catalog/Domains/Products/Models/Pricing.cs`).

## Logging

- Use structured logging with message templates: `logger.LogInformation("Product {ProductId} published", id)`.
  No string interpolation in log calls.
- Never log secrets, tokens, passwords, OTP codes or full PII.
- Use `Warning` for handled, expected-but-notable cases, and `Error` for failures needing attention.

## Localization

- Every user-facing message comes from the module resx through `IStringLocalizer<<Module>Resource>`, with keys in
  **both** `.resx` and `.ar.resx`. Unit tests check that the key sets are identical (see Catalog `ValidatorTests`).
- Bilingual data is `LocalizedText` (en/ar). Enums exposed to clients carry `[LocalizedDescription("en", "ar")]`.

## Naming

The full table is in `CLAUDE.md` → *Naming*, enforced by `NamingConventionTests`. Highlights:
- `<Feature>EndPoint` (capital P), `<Feature>CommandValidator` placed before `<Feature>Handler` in the same file.
- Test methods are named `Scenario_Expectation`.
- Private fields are `_camelCase`. Async methods end in `Async`. Constants are `PascalCase`.

## Comments

- Write XML doc comments (`///`) on public types whose *why* isn't obvious: invariants, concurrency, tenancy and
  idempotency decisions. Catalog is the model.
- Comment the reason, not the mechanics. No commented-out code; git has history.

## Formatting

Follow `.editorconfig`. Run `dotnet format StarterKit.slnx --verify-no-changes` before review if you touched many
files.
