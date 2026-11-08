using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Catalog.CatalogModuleManifest))]

namespace Catalog;

/// <summary>The Catalog module's manifest: discovered by the host (ADR-0019), delegates to <see cref="CatalogModule"/>.</summary>
public sealed class CatalogModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Catalog;

    public override IEnumerable<Assembly> EnumAssemblies => [Catalog.Contracts.CatalogContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddCatalogModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseCatalogModule();
}
