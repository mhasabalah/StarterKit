using System.Reflection;

namespace Notification.Contracts;

/// <summary>
/// Assembly marker for Notification.Contracts. Registered in <c>AddEnumLookup(...)</c> (src/Api/Program.cs)
/// so the enums below are served by the generic enum lookup endpoint.
/// </summary>
public static class NotificationContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
