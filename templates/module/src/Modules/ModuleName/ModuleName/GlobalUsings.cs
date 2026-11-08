global using System.Reflection;
global using Carter;
global using FluentValidation;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Localization;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Mediation;
global using Shared.Contracts.Pagination;
global using Shared.Core.Data;
global using Shared.Core.DDD;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.MultiTenancy;
global using Shared.Core.MultiTenancy.Web;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using ModuleName.Data;
global using ModuleName.Resources;
#if (EXAMPLE)
global using ModuleName.Contracts.Items;
global using ModuleName.Data.Repository;
#endif

namespace ModuleName;

/// <summary>Assembly marker for the ModuleName module (mediator, Carter and Mapster scanning).</summary>
public static class ModuleNameRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
