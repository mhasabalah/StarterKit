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
global using Search.Contracts.Indexing;
global using Search.Contracts.Querying;
global using Search.Data;
global using Search.Engine;
global using Search.Options;
global using Search.Resources;

namespace Search;

/// <summary>Assembly marker for the Search module (mediator, Carter and Mapster scanning).</summary>
public static class SearchRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
