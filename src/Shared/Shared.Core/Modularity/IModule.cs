using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Shared.Core.Modularity;

/// <summary>
/// The manifest of one business module: everything the host needs to plug it in. A module assembly declares exactly
/// one manifest with <see cref="ModuleManifestAttribute"/>; the host discovers it through <see cref="ModuleCatalog"/>
/// (ADR-0019), so adding a module never edits <c>Program.cs</c>.
/// </summary>
public interface IModule
{
    /// <summary>Module name = the implementation assembly name (e.g. <c>Catalog</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Registration and initialization order (ascending, ties by <see cref="Name"/>). Use <see cref="ModuleOrder"/>.
    /// </summary>
    int Order { get; }

    /// <summary>Assemblies scanned for MassTransit consumers (normally the module's own assembly).</summary>
    IEnumerable<Assembly> ConsumerAssemblies { get; }

    /// <summary>Assemblies whose enums the enum lookup endpoint serves (normally the module's Contracts).</summary>
    IEnumerable<Assembly> EnumAssemblies { get; }

    /// <summary>Registers the module's services (the <c>Add&lt;M&gt;Module</c> step).</summary>
    void AddServices(IHostApplicationBuilder builder);

    /// <summary>
    /// Initializes the module once the app is built: migrations + seeds, recurring jobs, extra endpoints (the
    /// <c>Use&lt;M&gt;Module</c> step). Never adds middleware: pipeline position belongs to <c>Program.cs</c>.
    /// </summary>
    void Initialize(WebApplication app);
}
