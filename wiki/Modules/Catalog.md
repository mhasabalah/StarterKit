# Catalog Module: the Reference Module

`Catalog` is the **reference module** of the template. It contains one example of every feature type a real
module needs (moderation workflow, soft delete, image gallery, bilingual lookups, Redis cache, MassTransit,
Hangfire, ...) on a generic `Product` aggregate. When you build a new module, find the pattern below and copy
the file it points to. Delete the module when your project no longer needs it (steps at the end).

All paths are relative to `src/Modules/Catalog/` unless they start with `src/` or `wiki/`.

## Layout

```
Catalog.Contracts/          public surface: commands, queries, DTOs, enums, integration events
Catalog/                    implementation (internal to the module)
  CatalogModule.cs          Add/UseCatalogModule: DI, DbContext, seeder, Hangfire job
  Common/                   module-wide helpers (culture, LIKE escaping, 23505, paging/sort, validation rules)
  Data/                     DbContext, design-time factory, configurations, repositories, seeder, migrations
  Domains/<Aggregate>/      Models, Features/<Feature>/{Handler,EndPoint}, Events, EventHandlers, Services, ...
  Options/  Resources/      bound options; en/ar resx
Catalog.Tests/              unit tests (xUnit + FluentAssertions 7 + Moq)
Catalog.IntegrationTests/   Testcontainers Postgres tests through the real DI + mediator pipeline
src/Api.E2ETests/Catalog/   HTTP end-to-end flows against the booted Api
```

## Pattern map ("copy me")

### Domain modelling

| Pattern | File |
|---|---|
| Aggregate root: factory, private setters, invariants, id-only domain events | `Catalog/Domains/Products/Models/Product.cs` |
| Aggregate version covers child-only changes (every mutation stamps `UpdatedAt`) | `Product.Touch()` in `Product.cs` |
| Owned value object with explicit columns + computed field (`DiscountPercentage`) | `Catalog/Domains/Products/Models/Pricing.cs`, `Data/Configurations/ProductConfiguration.cs` (`OwnsOne`) |
| Bilingual name (`Shared.Contracts.Localization.LocalizedText`) as owned type → `name_en` / `name_ar` | `ProductConfiguration.cs`, `Data/Configurations/LookupConfigurations.cs` |
| Child entity collection with client ids (`ValueGeneratedNever`) | `Catalog/Domains/Products/Models/ProductChildren.cs` (`ProductImage`), `Data/Configurations/ProductChildConfigurations.cs` |
| Many-to-many join entity **with payload** (attribute value) | `ProductAttribute` in `ProductChildren.cs`; `Product.AddAttribute/RemoveAttribute/ClearAttributes/SyncAttributes` |
| Many-to-many pure join entity | `ProductTag` in `ProductChildren.cs`; `Product.AddTag/RemoveTag/ClearTags/SyncTags` |
| Image gallery rules (max size, exactly one primary, contiguous order, reorder = permutation) | `Product.AddImage/RemoveImage/SetPrimaryImage/ReorderImages` |
| State machine as an allow-list (+ owner-editable statuses) | `Catalog/Domains/Products/Models/ProductStatusTransitions.cs` |
| Workflow methods with reviewer fields and one event per transition | `Product.SubmitForReview/Approve/Reject/RequestChanges/Publish/Archive/ReturnToDraft` |
| True soft delete + restore + purge marker | `Product.Delete/Restore/MarkPurged` |
| Hierarchical lookup (self-referencing parent) + pure tree algorithms (cycle check, descendants, tree build) | `Catalog/Domains/Categories/Models/Category.cs`, `CategoryHierarchy.cs` |
| Lookup with `int[]` "applicable types" (Npgsql array `Contains`) and a data-type enum validating values | `Catalog/Domains/AttributeTypes/Models/AttributeType.cs` |
| Simple lookup entity (deactivate, never delete) | `Catalog/Domains/Tags/Models/Tag.cs` |
| Separate small aggregate with unique `{UserId, ProductId}` | `Catalog/Domains/Wishlist/Models/WishlistItem.cs` |

Workflow: `Draft / ChangesRequested / Rejected → PendingReview → Approved | Rejected | ChangesRequested`,
`Approved → Published → Archived → Draft`. Owners may edit content only in Draft / ChangesRequested / Rejected;
admins at any status.

### Contracts

| Pattern | File |
|---|---|
| Commands / queries / results (naming enforced by `src/Api.Tests`) | `Catalog.Contracts/Products/Commands/*`, `Products/Queries/*` |
| Shared field interface so create + update share one validator | `IProductDetails` in `Catalog.Contracts/Products/Commands/CreateProductCommand.cs` |
| Filter interface shared by public + admin list queries | `Catalog.Contracts/Products/Queries/IProductFilterQuery.cs` |
| Public / owner / admin DTOs by inheritance (`ProductDto` → `MyProductDto` → `ProductAdminDto`) | `Catalog.Contracts/Products/Dtos/*` |
| Every read DTO exposes `long Version`; critical updates take `long? Version = null` | `ProductDto`, `UpdateProductCommand`, `ProductWorkflowCommands.cs` |
| Enums with `[LocalizedDescription]` (+ string JSON) and `*Label` fields in DTOs | `Catalog.Contracts/Products/Enums/ProductEnums.cs` |
| Contracts assembly marker for `AddEnumLookup` | `Catalog.Contracts/CatalogContractsRoot.cs` (registered in `src/Api/Program.cs`) |
| Cross-module contract query with no endpoint | `GetProductStatsQuery` → consumed by `src/Api/Endpoints/DashboardEndPoint.cs` |
| Integration event (`Shared.Contracts.Messaging.IntegrationEvent` + `[MessageUrn("catalog.product-published.v1")]`) | `Catalog.Contracts/Products/IntegrationEvents/ProductPublishedIntegrationEvent.cs` |

### Persistence

| Pattern | File |
|---|---|
| DbContext: schema `catalog`, `FilterSoftDeletedProperties`, snake_case, version tokens | `Catalog/Data/CatalogDbContext.cs` |
| Join/child following the parent's soft delete (non-`IEntity` with its own filter) | `WishlistItemConfiguration` in `Data/Configurations/LookupConfigurations.cs` |
| Enums stored as strings, explicit unique index names | `Data/Configurations/*.cs` |
| Repository per aggregate root (+ `IgnoreQueryFilters` variants for restore/purge) | `Catalog/Data/Repository/ProductRepository.cs`, `CategoryRepository.cs`, `WishlistRepository.cs` |
| Postgres `23505` (unique violation) → HTTP 409, matched by index name | `Shared.Core/Data/PostgresErrors.cs` (`ex.IsUniqueViolation(<Configuration>.<Index>Name)`), used in the repositories and lookup handlers |
| Idempotent seeder (`IgnoreQueryFilters().AnyAsync()` guard, fixed ids) | `Catalog/Data/Seed/CatalogDataSeeder.cs` |
| Design-time factory for `dotnet ef` (connection name `starterkit`) | `Catalog/Data/CatalogDbContextFactory.cs` |

### Features (vertical slices: `Features/<Feature>/<Feature>Handler.cs` + `<Feature>EndPoint.cs`)

| Pattern | File |
|---|---|
| Create with owner anti-spoofing, lookup validation, pending-upload session association (non-fatal) | `Features/CreateProduct/CreateProductHandler.cs` |
| Update with `dbContext.ExpectVersion(entity, command.Version)` and delta sync of children | `Features/UpdateProduct/UpdateProductHandler.cs` |
| Shared skeleton for workflow commands (load → authorize → expect version → transition → save) | `Catalog/Domains/Products/Services/ProductWorkflowRunner.cs`, `Features/{SubmitProductForReview,ApproveProduct,RejectProduct,RequestProductChanges,PublishProduct,ArchiveProduct,UnarchiveProduct}` |
| Soft delete / admin restore / internal purge command | `Features/DeleteProduct`, `Features/RestoreProduct`, `Features/PurgeProduct` |
| Admin toggle (non-critical: no version) | `Features/ToggleProductFeatured` |
| Multipart upload via the Attachment module (`DisableAntiforgery`, rate limit `upload`, compensation on failure) | `Features/UploadProductImage` |
| Set primary / delete (file deleted after the gallery change commits) / reorder images | `Features/SetPrimaryProductImage`, `Features/DeleteProductImage`, `Features/ReorderProductImages` |
| Public list: forced `Published`, `PageGate` policy, rate limit `search` | `Features/GetProducts` |
| Admin list (+ `IncludeDeleted`) / admin detail | `Features/GetAdminProducts`, `Features/GetAdminProductById` |
| Dedicated search endpoint (rate limit `search`) | `Features/SearchProducts` |
| Public detail: owner sees own drafts; atomic view counter (`ExecuteUpdateAsync`, no version bump) | `Features/GetProductById/GetProductByIdHandler.cs` |
| "My products" + stats in one `GroupBy(_ => 1)` query | `Features/GetMyProducts/GetMyProductsHandler.cs` |
| Admin moderation queue (FIFO) | `Features/GetPendingProducts` |
| Grouped Carter module for small lookup CRUD (`<X>Module`) | `Domains/Categories/Features/CategoryEndPoints.cs`, `Domains/Tags/Features/TagsModule.cs`, `Domains/AttributeTypes/Features/AttributeTypesModule.cs`, `Domains/Wishlist/Features/WishlistModule.cs` |
| Lookup CRUD with "deactivate instead of delete" + `IncludeInactive` (admins only) | `Domains/Tags/Features/TagHandlers.cs`, `Domains/AttributeTypes/Features/AttributeTypeHandlers.cs`, `Domains/Categories/Features/*` |
| Re-parenting with cycle detection + version | `Domains/Categories/Features/UpdateCategory/UpdateCategoryHandler.cs` |
| Wishlist add (409 on duplicate, unique index for races) / remove / list (`IsAvailable`) | `Domains/Wishlist/Features/WishlistHandlers.cs` |

### Queries and mapping

| Pattern | File |
|---|---|
| One filter pipeline for public + admin (category descendants, price, tags/attributes "has all", owner, featured, escaped ILIKE search) | `Catalog/Domains/Products/Services/ProductQueryBuilder.cs` |
| One SQL projection row → audience DTOs in memory (labels, discount) — DRY instead of one projection per DTO | `Catalog/Domains/Products/Mappings/ProductProjections.cs`, `ProductDtoMapper.cs` |
| Culture chosen in SQL (`arabic ? p.Name.Ar : p.Name.En` as a parameter) | `ProductProjections.ToProductRows` |
| `IsWishlisted` projected per current user | `ProductProjections.ToProductRows` |
| Default sort (newest first) + `OrderByDynamic` + stable tie-breaker; `PaginatedResult<T>` | `Catalog/Common/PaginatedResultExtensions.cs` |
| Mapster `IRegister` + `ProjectToType` for simple projections; culture filled in memory | `Catalog/Domains/Products/Mappings/LookupMappingConfig.cs`, `GetTagsHandler`, `GetCategoriesHandler` |
| LIKE-wildcard escaping | `Catalog/Common/LikePattern.cs` |

### Cross-cutting

| Pattern | File |
|---|---|
| Resource authorization service (owner/admin, status lock, owner anti-spoofing) | `Catalog/Domains/Products/Services/ProductAuthorizationService.cs` |
| Cross-module validation through another module's Contracts (attachment must be an image) | `Catalog/Domains/Products/Services/ProductImageValidator.cs` |
| DB-backed reference validation (category/tags/attributes active, applicable, value matches data type) | `Catalog/Domains/Products/Services/ProductLookupValidator.cs` |
| Redis (`IDistributedCache`) cache of a culture-neutral snapshot; failures swallowed with a warning | `Catalog/Domains/Categories/Cache/CategoryTreeCache.cs` |
| Cache invalidation via domain event (dispatched after commit) | `Catalog/Domains/Categories/EventHandlers/CategoryChangedEventHandler.cs` |
| Integration event raised by the aggregate (`AddIntegrationEvent`) → transactional outbox (`builder.AddTenantOutbox()`, `services.AddOutbox<CatalogDbContext>()`, migration `AddOutbox` with RLS) | `Product.Publish()`, `Catalog/Data/CatalogDbContext.cs`, `CatalogModule.cs` - see `wiki/Architecture/Messaging.md` |
| Sample MassTransit consumer (auto-registered via `AddMassTransitWithAssemblies(CatalogRoot...)`) | `Catalog/Domains/Products/Consumers/ProductPublishedConsumer.cs` |
| Cleanup of another module's data on hard delete (`DeleteAttachmentsByEntityCommand`, non-fatal) | `Catalog/Domains/Products/EventHandlers/ProductPurgedEventHandler.cs` |
| Hangfire recurring job with options bound from config (`Catalog` section) | `Catalog/Domains/Products/Jobs/PurgeSoftDeletedProductsJob.cs`, `Catalog/Options/CatalogOptions.cs`, `CatalogModule.UseCatalogModule` |
| en/ar resx + validators using `IStringLocalizer<CatalogResource>` | `Catalog/Resources/CatalogResource*.resx`, any `*Validator` |
| Reusable validation rules (bilingual name, paging, version, filters) | `Catalog/Common/CatalogValidationRules.cs`, `Domains/Products/Features/ProductFilterRules.cs` |

Why attachments are cleaned up on **purge**, not on soft delete: a restored product must still have its images.

## Endpoints

| Method | Route (`/api/v1`) | Auth |
|---|---|---|
| POST | `/products` | authenticated, rate limit `create` |
| PUT / DELETE | `/products/{id}` | owner (editable status) or admin / owner or admin |
| GET | `/products`, `/products/search` | `PageGate` (page 1 public), rate limit `search` |
| GET | `/products/{id}` | public (owners also see their unpublished products) |
| GET | `/products/my`, `/products/wishlist` | authenticated |
| POST | `/products/{id}/submit-for-review`, `/archive`, `/unarchive` | owner or admin |
| POST | `/products/{id}/approve`, `/reject`, `/request-changes`, `/publish` | admin |
| POST / PUT / DELETE | `/products/{id}/images/upload`, `/images/{imageId}/set-primary`, `/images/reorder`, `/images/{imageId}` | owner (editable status) or admin |
| POST / DELETE | `/products/{id}/wishlist` | authenticated |
| GET | `/admin/products`, `/admin/products/{id}`, `/admin/products/pending` | admin |
| POST / PATCH | `/admin/products/{id}/restore`, `/admin/products/{id}/featured` | admin |
| GET | `/categories/tree` | public (cached) |
| GET / POST / PUT / DELETE | `/categories` | `catalog.categories.manage` (tenant permission; Owner and Administrator hold it) |
| GET / POST / PUT / DELETE | `/tags`, `/attribute-types` | GET public; everything else admin |

Bulk: `catalog.categories` import/export definitions (`Domains/Categories/DataTransfer`), see [DataTransfer](DataTransfer.md).

## Tests

| Kind | Where | Covers |
|---|---|---|
| Unit | `Catalog.Tests/Domain/*` | invariants, every state-machine transition (Theory over all status × action pairs), gallery rules, pricing, soft delete, hierarchy algorithms, attribute value rules |
| Unit | `Catalog.Tests/Features/*`, `Services/*` | validators (incl. en/ar messages and resx key parity), handlers (ExpectVersion via a disconnected DbContext, owner/admin/status authorization, upload compensation), cache (invalidate, swallow failures), mapper, LIKE escaping |
| Integration | `Catalog.IntegrationTests/*` | real `AddCatalogModule` + Postgres: filters, has-all, search escaping, sort/paging, public vs admin, my-products stats, soft-delete filter + restore + purge job cascade, wishlist unique index, atomic view counter, stale version → `ConcurrencyConflictException`, cache invalidation via events, seeder idempotency, integration event published |
| E2E | `src/Api.E2ETests/Catalog/ProductCrudFlowTests.cs` | seller → moderation → public → wishlist → archive, 403s, 409, soft delete + restore, lookups |

## Wiring checklist for a new module (based on Catalog)

1. Projects: `<M>.Contracts`, `<M>`, `<M>.Tests` (+ `<M>.IntegrationTests`); add them to `StarterKit.slnx`.
2. References: `<M>.Contracts` → `Shared.Contracts` ONLY (it has `[LocalizedDescription]`, `IDomainEvent`, `IntegrationEvent`; architecture test);
   `<M>` → `Shared.Core`, `<M>.Contracts`, other modules' `.Contracts` only (enforced by `src/Api.Tests`).
3. `GlobalUsings.cs` + `<M>Root` marker in the implementation, `<M>ContractsRoot` in Contracts.
4. DbContext like `CatalogDbContext` (call `FilterSoftDeletedProperties()` only if you soft delete) + design-time factory.
   Business data is tenant data: implement `ITenantDataContext`, make every entity `IMustHaveTenant`, end
   `OnModelCreating` with `ApplyTenantFilters(builder)` and enable RLS in the migration - checklist in
   `wiki/MultiTenancy/Adding-a-Tenant-Aware-Module.md`. Add the module to `ModuleBoundaryTests.Modules`.
5. `<M>Module.cs` like `CatalogModule.cs` (repositories, services, `AddMediator`, `AddCustomMapster`, `AddCarterWithAssemblies`,
   `AddCustomDbContext(..., "starterkit")`, seeder, jobs; `Use<M>Module` migrates and schedules jobs).
6. `src/Api/Program.cs`: `AddMassTransitWithAssemblies(configuration, <M>Root...)` (consumers), `AddEnumLookup(<M>ContractsRoot...)`,
   `Add<M>Module(configuration)`, `Use<M>Module()`; `src/Api/Api.csproj` → `<M>`.
7. Generate the first migration (see `wiki/Reference/Migrations.md`). Publishing integration events? Add the outbox
   (`builder.AddOutbox()`/`AddTenantOutbox()`, `services.AddOutbox<TContext>()`) - checklist in `wiki/Architecture/Messaging.md`.
8. Tests at every level as in the table above; `dotnet build StarterKit.slnx` and `dotnet test StarterKit.slnx`.

## Removing the Catalog sample

1. Remove `AddCatalogModule` / `UseCatalogModule`, `CatalogRoot` (MassTransit) and `CatalogContractsRoot` (enum lookup) from `src/Api/Program.cs`.
2. Remove the product stats from `src/Api/Endpoints/DashboardEndPoint.cs` and the `Catalog` section from `src/Api/appsettings.json`.
3. Remove the `Catalog` references from `src/Api/Api.csproj` and `src/Api.E2ETests/Api.E2ETests.csproj`; delete `src/Api.E2ETests/Catalog/`.
4. In `src/Api.Tests/Architecture`: remove `"Catalog"` from `ModuleBoundaryTests.Modules`; remove
   `ProductPublishedIntegrationEvent` from the expected list and `CatalogDbContext` from the outbox expectation in
   `MessagingArchitectureTests`; adjust `TenantIsolationArchitectureTests.Catalog_and_Attachment_are_tenant_data_contexts`.
   Optionally remove `Permissions.CatalogModerate` / `TenantPermissions.ProductsManage` and their grants.
5. Remove the `/src/Modules/Catalog/` folder from `StarterKit.slnx` and delete `src/Modules/Catalog/`.
6. Drop the schema from existing databases (`DROP SCHEMA catalog CASCADE;`) and the Hangfire job `catalog-purge-soft-deleted-products`.
7. Run `dotnet build StarterKit.slnx`.
