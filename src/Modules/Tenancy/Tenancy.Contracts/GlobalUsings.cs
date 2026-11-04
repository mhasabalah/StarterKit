global using Shared.Contracts.Mediation;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Pagination;
global using Shared.Contracts.Events;
global using Shared.Core.MultiTenancy;
global using System.Reflection;

namespace Tenancy.Contracts;

public static class TenancyContractsRoot
{
    public static Assembly AssemblyName => typeof(TenancyContractsRoot).Assembly;
}
