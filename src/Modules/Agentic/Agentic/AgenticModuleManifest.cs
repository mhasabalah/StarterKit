using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Agentic.AgenticModuleManifest))]

namespace Agentic;

/// <summary>
/// The Agentic module's manifest: the host discovers it from the build graph and registers the module, its
/// MassTransit consumers and its enums (ADR-0019).
/// </summary>
public sealed class AgenticModuleManifest : ModuleBase
{
    /// <summary>After every business module (their agents and tools are registered first), before Notification.</summary>
    public override int Order => ModuleOrder.Agentic;

    /// <summary>Enums served by the enum lookup endpoint: add <c>[LocalizedDescription]</c> to the ones clients see.</summary>
    public override IEnumerable<Assembly> EnumAssemblies => [Contracts.AgenticContractsRoot.AssemblyName];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddAgenticModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseAgenticModule();
}
