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
global using Microsoft.Extensions.Localization;
global using Microsoft.Extensions.Logging;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Enums;
global using Shared.Contracts.Localization;
global using Shared.Contracts.Pagination;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.Data.Seed;
global using Shared.Core.DDD;
global using Shared.Contracts.Events;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.Localization.Services;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Catalog.Common;
global using Catalog.Contracts.AttributeTypes;
global using Catalog.Contracts.AttributeTypes.Enums;
global using Catalog.Contracts.Categories;
global using Catalog.Contracts.Products.Commands;
global using Catalog.Contracts.Products.Dtos;
global using Catalog.Contracts.Products.Enums;
global using Catalog.Contracts.Products.Queries;
global using Catalog.Contracts.Tags;
global using Catalog.Contracts.Wishlist;
global using Catalog.Data;
global using Catalog.Data.Repository;
global using Catalog.Domains.AttributeTypes.Models;
global using Catalog.Domains.Categories.Models;
global using Catalog.Domains.Products.Exceptions;
global using Catalog.Domains.Products.Models;
global using Catalog.Domains.Products.Services;
global using Catalog.Domains.Tags.Models;
global using Catalog.Domains.Wishlist.Models;
global using Catalog.Resources;

namespace Catalog;

public static class CatalogRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
