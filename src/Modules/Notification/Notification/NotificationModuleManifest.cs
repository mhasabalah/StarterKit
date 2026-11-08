using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Notification.NotificationModuleManifest))]

namespace Notification;

/// <summary>The Notification module's manifest: discovered by the host (ADR-0019), delegates to <see cref="NotificationModule"/>.</summary>
public sealed class NotificationModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Notification;

    public override IEnumerable<Assembly> EnumAssemblies => [Notification.Contracts.NotificationContractsRoot.AssemblyName, NotificationRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddNotificationModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseNotificationModule();
}
