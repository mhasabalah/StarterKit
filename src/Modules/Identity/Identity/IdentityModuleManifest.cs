using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Identity.IdentityModuleManifest))]

namespace Identity;

/// <summary>The Identity module's manifest: discovered by the host (ADR-0019), delegates to <see cref="IdentityModule"/>.</summary>
public sealed class IdentityModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Identity;

    public override IEnumerable<Assembly> EnumAssemblies => [IdentityRoot.AssemblyName, typeof(Identity.Contracts.Sso.SsoProtocol).Assembly];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddIdentityModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseIdentityModule();
}
