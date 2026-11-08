namespace Shared.Core.Modularity;

/// <summary>
/// Marks a module implementation assembly and names its <see cref="IModule"/> manifest:
/// <c>[assembly: ModuleManifest(typeof(CatalogModuleManifest))]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModuleManifestAttribute(Type manifestType) : Attribute
{
    public Type ManifestType { get; } = manifestType;
}

/// <summary>
/// Names a module assembly the host references. Never written by hand: <c>src/Api/Api.csproj</c> generates one per
/// module <c>ProjectReference</c>, so the module list is the build graph.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAssemblyAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
