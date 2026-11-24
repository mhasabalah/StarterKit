using Attachment.Contracts.Authorization;
using Catalog.Data.Seed;
using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Categories.DataTransfer;
using Catalog.Domains.Products.DataTransfer;
using DataTransfer.Contracts.Definitions;
using Catalog.Domains.Products.Jobs;
using Catalog.Domains.Products.Mappings;
#if (SEARCH)
using Catalog.Domains.Products.SearchIndex;
using Search.Contracts.Indexing;
#endif
using Catalog.Options;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Messaging.Outbox;
using Shared.Core.Mapster;

namespace Catalog;

/// <summary>
/// Catalog module — the reference module for new modules (see wiki/Modules/Catalog.md for the pattern map).
/// The host discovers it through <see cref="CatalogModuleManifest"/> (consumers, enums, order; ADR-0019).
/// </summary>
public static class CatalogModule
{
    public const string PurgeJobId = "catalog-purge-soft-deleted-products";
    public const string AuditPurgeJobId = "catalog-purge-audit-entries";

    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));

        // Repositories (one per aggregate root)
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();

        // Domain services
        services.AddScoped<IProductAuthorizationService, ProductAuthorizationService>();
        services.AddScoped<IProductImageValidator, ProductImageValidator>();
        services.AddScoped<IProductLookupValidator, ProductLookupValidator>();
        services.AddScoped<ProductWorkflowRunner>();
        services.AddScoped<ProductDtoMapper>();
        services.AddScoped<SqlProductSearch>();
#if (SEARCH)
        // Cross-module: products are searchable through the Search module (index catalog.products, ADR-0036).
        services.AddSearchIndex<ProductSearchDocumentSource>(ProductSearchIndex.Definition);
        services.AddScoped<IProductSearch, IndexedProductSearch>();
#else
        services.AddScoped<IProductSearch>(sp => sp.GetRequiredService<SqlProductSearch>());
#endif
        services.AddScoped<ICategoryTreeCache, CategoryTreeCache>(); // IDistributedCache comes from AddRedisCache

        // Cross-module: tell the Attachment module who may attach to / read files of a Product (deny-by-default there).
        services.AddAttachmentEntityPolicy<ProductAttachmentAccessPolicy>(
            Attachment.Contracts.Attachments.Dtos.EntityType.Product);

        // Cross-module: bulk CSV/Excel import/export of products through the DataTransfer module (catalog.products).
        services.AddImportDefinition<ProductImportDefinition>();
        services.AddExportDefinition<ProductExportDefinition>();
        services.AddImportDefinition<CategoryImportDefinition>();
        services.AddExportDefinition<CategoryExportDefinition>();

        services.AddMediator(CatalogRoot.AssemblyName);
        services.AddCustomMapster(CatalogRoot.AssemblyName);
        services.AddCarterWithAssemblies(CatalogRoot.AssemblyName);

        services.AddCustomDbContext<CatalogDbContext>(configuration, Shared.Core.Data.DatabaseDefaults.ConnectionStringName);
        services.AddOutbox<CatalogDbContext>(); // delivers the integration events raised by Catalog aggregates
        services.AddScoped<IDataSeeder, CatalogDataSeeder>();

        // Hangfire jobs
        services.AddTransient<PurgeSoftDeletedProductsJob>();
        services.AddTransient<PurgeAuditEntriesJob>();

        return services;
    }

    public static IApplicationBuilder UseCatalogModule(this IApplicationBuilder app)
    {
        app.UseMigration<CatalogDbContext>();

        using var scope = app.ApplicationServices.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<CatalogOptions>>().Value;
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<PurgeSoftDeletedProductsJob>(
            PurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.PurgeCron);

        recurringJobManager.AddOrUpdate<PurgeAuditEntriesJob>(
            AuditPurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            Cron.Daily(4));

        return app;
    }
}
