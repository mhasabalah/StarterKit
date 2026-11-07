using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Subscriptions.SubscriptionsModuleManifest))]

namespace Subscriptions;

/// <summary>The Subscriptions module's manifest: discovered by the host (ADR-0019), delegates to <see cref="SubscriptionsModule"/>.</summary>
public sealed class SubscriptionsModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Subscriptions;

    public override IEnumerable<Assembly> EnumAssemblies => [SubscriptionsRoot.AssemblyName, typeof(Subscriptions.Contracts.Subscriptions.Enums.SubscriptionStatus).Assembly];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddSubscriptionsModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseSubscriptionsModule();
}
