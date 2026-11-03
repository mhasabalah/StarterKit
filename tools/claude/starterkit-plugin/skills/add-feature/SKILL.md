---
name: add-feature
description: Add a vertical-slice feature (command or query) to a StarterKit module - contract record + result in .Contracts, validator + handler in one file, Carter endpoint, paginated projection query, optimistic concurrency for updates, localized validation messages and tests. Use for "add an endpoint", "add CRUD for X", "create/update/delete/get X feature", "new command/query".
---

# Add a feature slice

One use case = one folder: `src/Modules/<M>/<M>/Domains/<Aggregate>/Features/<Feature>/` holding
`<Feature>Handler.cs` (validator + handler) and `<Feature>EndPoint.cs` (Carter). Contracts (command/query, result,
DTO) live in `<M>.Contracts/<Aggregate>s/{Commands,Queries,Dtos}/`.

**Not for:** a new aggregate/entity (run `add-aggregate` first), cross-module reactions (`add-integration-event`).

## Inputs

- Module, aggregate, feature name (`Create<X>`, `Update<X>`, `Delete<X>`, `Get<X>s`, `Get<X>ById`, or a verb like
  `Publish<X>`).
- Who may call it → auth (see table below).
- For updates: is concurrent editing a real risk? (profile, financial config, status transitions → yes → version).
- Request/response shape and validation rules.

## Steps

Templates: [references/templates.md](references/templates.md).

1. **Contract** (`<M>.Contracts`):
   - Command: `public record Create<X>Command(...) : ICommand<Create<X>Result>;` + `public record Create<X>Result(Guid Id);`
   - Query: `public record Get<X>sQuery : BaseQuery, IQuery<PaginatedResult<<X>Dto>> { filters { get; init; } }`
   - DTO `<X>Dto` - include `long Version` when the aggregate has a versioned update.
   - Contracts never reference `Shared.Core`; only `Shared.Contracts` types (`LocalizedText`, `PaginatedResult`, ...).
2. **Handler file** `<Feature>Handler.cs`: `<Feature>CommandValidator` (or `<Feature>QueryValidator`) FIRST, then
   `<Feature>Handler`. Validation messages come from `IStringLocalizer<<M>Resource>`; add every new key to BOTH
   `Resources/<M>Resource.resx` and `<M>Resource.ar.resx`.
   - Command handlers take the **repository** (`I<X>Repository`), load the aggregate, call a domain method, save.
     Business rules live in the entity, not the handler. Throw module exceptions (`<X>NotFoundException`, ...).
   - Query handlers take the **DbContext** directly, `AsNoTracking()`, project to DTO in SQL, then
     `.PagedResult(query.PageNumber, query.PageSize, ct)`. Small bounded lookups may return `List<T>`.
   - Updates with concurrency: `long? Version = null` on the command/request, `dbContext.ExpectVersion(entity,
     command.Version)` after loading and before saving, return the new `entity.Version`. Never set Version yourself.
   - Cross-module data: `ISender.Send(<OtherModule>.Contracts query)` only. Users: `UserDirectory.Contracts`.
   - Need email/WhatsApp? `INotificationSender` (see `add-email-template`).
3. **Endpoint** `<Feature>EndPoint.cs`: `ICarterModule`, route `EndpointConfig.BaseApiPath + "/<xs>"`,
   request/response records in the same file, map request → command (route id wins over body), wrap with
   `ApiResponseExtensions.Success(...)`, full `.Produces<...>` list, `.WithName/.WithTags/.WithSummary`,
   `.WithApiVersionSet(app.NewApiVersionSet("<M>").Build())`, `.HasApiVersion(1.0)`, auth, rate limiting.
   Several small endpoints of one aggregate may instead be grouped in a `<X>sModule : ICarterModule` (see
   `Catalog/Domains/Tags/Features/TagsModule.cs`).
4. **DI**: nothing for handlers/validators/endpoints (assembly scanning). New services → `<M>Module.cs`.
5. **Tests** (see `write-tests`): validator tests, handler unit tests with Moq (happy path + not found + forbidden +
   conflict), and an integration test through `fixture.SendAs(...)` for anything touching SQL/concurrency/tenancy.

## Auth - always explicit

Carter endpoints are deny-by-default (an undeclared endpoint requires an authenticated user), but
`AdminEndpointAuthorizationTests.Every_endpoint_declares_authorization_explicitly` fails unless every endpoint
declares one of:

| Caller | Add |
|---|---|
| Any signed-in user | `.RequireAuthorization()` (+ ownership check in a service, e.g. `IProductAuthorizationService`) |
| Public list/search (page 1 anonymous) | `.RequireAuthorization("PageGate")` |
| Platform staff feature | `.RequirePermission(Permissions.X)` - see `add-permission` |
| Tenant member with a tenant permission | `.RequireTenantPermission(TenantPermissions.X)` / `.RequireTenantMember()` |
| Platform admin over tenants | `.HostOnly().RequirePermission(Permissions.TenantsManage)` |
| Destructive / owner action | also `.DisallowImpersonation()` |
| Truly public | `.AllowAnonymous()` - explicit, never implicit |

Rate limiting policies (appsettings `RateLimiting:Policies`): `create`, `search`, `upload`, `auth`, `otp`.

## Naming (enforced by `NamingConventionTests`)

`Create<X>Command` · `Create<X>CommandValidator` · `Create<X>Handler` · `Create<X>Result` · `Create<X>Request` ·
`Create<X>Response` · `Create<X>EndPoint` (capital **P**) · `Get<X>sQuery` / `Get<X>ByIdQuery` · `<X>Dto`.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Modules/<M>/<M>.Tests --filter "FullyQualifiedName~<Feature>"
dotnet test src/Modules/<M>/<M>.IntegrationTests --filter "FullyQualifiedName~<Feature>"   # Docker
dotnet test src/Api.Tests                                                                   # guardrails
```

Optionally run the API and call the endpoint from `/swagger` or `/scalar`. Stale `version` must return **409**,
invalid input **400** with localized messages (`Accept-Language: ar`), missing auth **401**.
