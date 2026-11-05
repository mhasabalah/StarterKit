using System.Reflection;

namespace Webhooks.Contracts;

/// <summary>Assembly marker for Webhooks.Contracts (enum lookup registration in src/Api/Program.cs).</summary>
public static class WebhooksContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
