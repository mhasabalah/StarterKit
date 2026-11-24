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

    /// <summary>The index status, the field types and the match modes the admin API and owners' DTOs return.</summary>
    public override IEnumerable<Assembly> EnumAssemblies => [Search.Contracts.SearchContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddSearchModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseSearchModule();
}
