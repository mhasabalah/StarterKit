global using System.IO;
global using System.Reflection;
global using System.Security.Claims;
global using System.IdentityModel.Tokens.Jwt;
global using System.Text;
global using Carter;
global using FluentValidation;
global using Mapster;
global using MediatR;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.AspNetCore.Authentication;
global using Microsoft.AspNetCore.Authentication.Google;
global using Microsoft.AspNetCore.Authentication.Facebook;
global using Microsoft.AspNetCore.Authentication.JwtBearer;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.IdentityModel.Tokens;
global using Shared.Contracts.Enums;
global using Shared.Contracts.Pagination;
global using Shared.Core.Extensions;
global using Shared.Core.Data;
global using Shared.Core.Data.Seed;
global using Shared.Core.DDD;
global using Shared.Contracts.Events;
global using Shared.Core.Exceptions;
global using Shared.Core.Response;
global using Shared.Contracts.CQRS;
global using Identity.Data.Repository.Onboarding;
global using Identity.Domains.Onboarding.Models;
global using Notification.Contracts;
global using Notification.Contracts.Enums;
global using Identity.Data;
global using Identity.Domains.Users.Models;
global using Identity.Domains.Roles.Models;
global using Identity.Domains.Roles.Constants;
global using Identity.Services.Sms;

namespace Identity;

public static class IdentityRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
