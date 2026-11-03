global using System.Reflection;
global using Carter;
global using FluentValidation;
global using MediatR;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Pagination;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.Data.Seed;
global using Shared.Core.DDD;
global using Shared.Contracts.Events;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.MultiTenancy;
global using Shared.Core.MultiTenancy.Authorization;
global using Shared.Core.MultiTenancy.Data;
global using Shared.Core.MultiTenancy.Web;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Tenancy.Contracts.Branches;
global using Tenancy.Contracts.Events;
global using Tenancy.Contracts.Members;
global using Tenancy.Contracts.Roles;
global using Tenancy.Contracts.Tenants;
global using Tenancy.Data;
global using Tenancy.Domains.Audit.Models;
global using Tenancy.Domains.Audit.Services;
global using Tenancy.Domains.Branches.Models;
global using Tenancy.Domains.Invitations.Models;
global using Tenancy.Domains.Members.Models;
global using Tenancy.Domains.Roles.Models;
global using Tenancy.Domains.Tenants.Exceptions;
global using Tenancy.Domains.Tenants.Models;

namespace Tenancy;

public static class TenancyRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
