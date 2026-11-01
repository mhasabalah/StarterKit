global using System.Reflection;
global using Carter;
global using FluentValidation;
global using Mapster;
global using MediatR;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Logging;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Pagination;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.DDD;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Catalog.Data;
global using Catalog.Data.Repository.Products;
global using Catalog.Domains.Products.Models;
global using Catalog.Domains.Products.Exceptions;
global using Catalog.Contracts.Products.Dtos;

namespace Catalog;

public static class CatalogRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
