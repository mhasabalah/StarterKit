using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Shipping.ShippingModuleManifest))]

namespace Shipping;

/// <summary>
/// The Shipping module's manifest: the host discovers it from the build graph and registers the module, its
/// MassTransit consumers and its enums (ADR-0019).
/// </summary>
public sealed class ShippingModuleManifest : ModuleBase
{
    /// <summary>After the platform modules, before Notification (see <see cref="ModuleOrder"/>).</summary>
    public override int Order => ModuleOrder.Default;

    /// <summary>Enums served by the enum lookup endpoint: add <c>[LocalizedDescription]</c> to the ones clients see.</summary>
    public override IEnumerable<Assembly> EnumAssemblies => [Contracts.ShippingContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddShippingModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseShippingModule();
}
