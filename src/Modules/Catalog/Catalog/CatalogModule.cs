using Attachment.Contracts.Authorization;
using Catalog.Data.Seed;
using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Products.Jobs;
using Catalog.Domains.Products.Mappings;
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
/// Program.cs must also pass <c>CatalogRoot.AssemblyName</c> to AddMassTransitWithAssemblies (consumers) and
/// <c>CatalogContractsRoot.AssemblyName</c> to AddEnumLookup (enums).
/// </summary>
public static class CatalogModule
{
    public const string PurgeJobId = "catalog-purge-soft-deleted-products";

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
        services.AddScoped<ICategoryTreeCache, CategoryTreeCache>(); // IDistributedCache comes from AddRedisCache

        // Cross-module: tell the Attachment module who may attach to / read files of a Product (deny-by-default there).
        services.AddAttachmentEntityPolicy<ProductAttachmentAccessPolicy>(
            Attachment.Contracts.Attachments.Dtos.EntityType.Product);

        services.AddMediatR(CatalogRoot.AssemblyName);
        services.AddCustomMapster(CatalogRoot.AssemblyName);
        services.AddCarterWithAssemblies(CatalogRoot.AssemblyName);

        services.AddCustomDbContext<CatalogDbContext>(configuration, "starterkit");
        services.AddOutbox<CatalogDbContext>(); // delivers the integration events raised by Catalog aggregates
        services.AddScoped<IDataSeeder, CatalogDataSeeder>();

        // Hangfire jobs
        services.AddTransient<PurgeSoftDeletedProductsJob>();

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

        return app;
    }
}
