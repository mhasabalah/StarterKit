using System.Reflection;

namespace Shipping.Contracts;

/// <summary>Assembly marker for Shipping.Contracts (enum lookup: <c>ShippingModuleManifest.EnumAssemblies</c>).</summary>
public static class ShippingContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
