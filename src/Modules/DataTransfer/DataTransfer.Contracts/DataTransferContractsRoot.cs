using System.Reflection;

namespace DataTransfer.Contracts;

/// <summary>Assembly marker for DataTransfer.Contracts (enum lookup registration in src/Api/Program.cs).</summary>
public static class DataTransferContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
