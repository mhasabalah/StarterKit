using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Search.SearchModuleManifest))]

namespace Search;

/// <summary>
/// The Search module's manifest: the host discovers it from the build graph and registers the module, its
/// MassTransit consumers and its enums (ADR-0019).
/// </summary>
public sealed class SearchModuleManifest : ModuleBase
{
    /// <summary>After the platform modules, before Notification (see <see cref="ModuleOrder"/>).</summary>
    public override int Order => ModuleOrder.Default;

    // No EnumAssemblies: the Search.Contracts enums describe index definitions and queries for module code, not values
    // clients pick from a lookup. Clients see search results through the owning modules' DTOs.

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddSearchModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseSearchModule();
}
