using System.Reflection;

namespace Catalog.Contracts;

/// <summary>
/// Assembly marker for Catalog.Contracts. Registered in <c>AddEnumLookup(...)</c> (src/Api/Program.cs)
/// so the enums below are served by the generic enum lookup endpoint.
/// </summary>
public static class CatalogContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
