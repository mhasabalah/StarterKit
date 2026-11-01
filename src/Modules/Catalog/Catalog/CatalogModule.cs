using Catalog.Domains.Products.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Mapster;

namespace Catalog;

/// <summary>
/// Catalog module — the reference vertical slice for new modules.
/// Copy this module's layout (Contracts + implementation + Tests) when adding a module.
/// </summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductImageValidator, ProductImageValidator>();

        services.AddMediatR(CatalogRoot.AssemblyName);
        services.AddCustomMapster(CatalogRoot.AssemblyName);
        services.AddCarterWithAssemblies(CatalogRoot.AssemblyName);

        services.AddCustomDbContext<CatalogDbContext>(configuration, "starterkit");

        return services;
    }

    public static IApplicationBuilder UseCatalogModule(this IApplicationBuilder app)
    {
        app.UseMigration<CatalogDbContext>();

        return app;
    }
}
