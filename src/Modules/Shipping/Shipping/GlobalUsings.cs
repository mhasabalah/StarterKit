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
global using Microsoft.Extensions.Logging;
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
global using Shipping.Contracts.Shipments;
global using Shipping.Data;
global using Shipping.Data.Repository;
global using Shipping.Domains.Shipments.Exceptions;
global using Shipping.Domains.Shipments.Models;
global using Shipping.Resources;

namespace Shipping;

/// <summary>Assembly marker for the Shipping module (mediator and Carter scanning).</summary>
public static class ShippingRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
