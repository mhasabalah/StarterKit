using System.Reflection;

namespace Agentic.Contracts;

/// <summary>Assembly marker for Agentic.Contracts (enum lookup: <c>AgenticModuleManifest.EnumAssemblies</c>).</summary>
public static class AgenticContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
