using System.Reflection;

namespace Search.Contracts;

/// <summary>Assembly marker for Search.Contracts (enum lookup: <c>SearchModuleManifest.EnumAssemblies</c>).</summary>
public static class SearchContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
