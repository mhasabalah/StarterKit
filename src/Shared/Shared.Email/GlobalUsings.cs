global using System.Reflection;
global using System.Text.Json;
global using Carter;
global using FluentValidation;
global using Hangfire;
global using MediatR;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Design;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.DDD;
global using Shared.Core.Extensions;
global using Shared.Core.Exceptions;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Shared.Email.Contracts;
global using Shared.Email.Enums;
global using Shared.Email.Jobs;
global using Shared.Email.Models;
global using Shared.Email.Persistence;
global using Shared.Email.Services;
global using Shared.Contracts.CQRS;

namespace Shared.Email;

public static class EmailRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
