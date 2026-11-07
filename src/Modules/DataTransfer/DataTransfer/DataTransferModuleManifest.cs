using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(DataTransfer.DataTransferModuleManifest))]

namespace DataTransfer;

/// <summary>The DataTransfer module's manifest: discovered by the host (ADR-0019), delegates to <see cref="DataTransferModule"/>.</summary>
public sealed class DataTransferModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.DataTransfer;

    public override IEnumerable<Assembly> EnumAssemblies => [DataTransfer.Contracts.DataTransferContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddDataTransferModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseDataTransferModule();
}
