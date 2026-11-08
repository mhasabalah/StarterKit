using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Webhooks.WebhooksModuleManifest))]

namespace Webhooks;

/// <summary>The Webhooks module's manifest: discovered by the host (ADR-0019), delegates to <see cref="WebhooksModule"/>.</summary>
public sealed class WebhooksModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Webhooks;

    public override IEnumerable<Assembly> EnumAssemblies => [Webhooks.Contracts.WebhooksContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddWebhooksModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseWebhooksModule();
}
