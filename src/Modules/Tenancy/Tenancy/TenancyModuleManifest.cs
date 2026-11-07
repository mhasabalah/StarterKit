using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Tenancy.TenancyModuleManifest))]

namespace Tenancy;

/// <summary>The Tenancy module's manifest: discovered by the host (ADR-0019), delegates to <see cref="TenancyModule"/>.</summary>
public sealed class TenancyModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Tenancy;

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddTenancyModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseTenancyModule();
}
