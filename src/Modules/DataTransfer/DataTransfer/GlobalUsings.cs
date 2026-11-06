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
global using Shared.Contracts.Events;
global using Shared.Contracts.Localization;
global using Shared.Contracts.Mediation;
global using Shared.Contracts.Pagination;
global using Shared.Core.Data;
global using Shared.Core.DDD;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.MultiTenancy;
global using Shared.Core.MultiTenancy.Authorization;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using DataTransfer.Contracts.Definitions;
global using DataTransfer.Contracts.Enums;
global using DataTransfer.Contracts.Jobs;
global using DataTransfer.Data;
global using DataTransfer.Data.Repository;
global using DataTransfer.Domains.TransferJobs.Exceptions;
global using DataTransfer.Domains.TransferJobs.Models;
global using DataTransfer.Engine;
global using DataTransfer.Options;
global using DataTransfer.Resources;

namespace DataTransfer;

public static class DataTransferRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
