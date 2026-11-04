global using System.Net.Http.Json;
global using System.Reflection;
global using System.Text.Json;
global using Carter;
global using FluentValidation;
global using Hangfire;
global using Shared.Contracts.Mediation;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Design;
global using Microsoft.Extensions.Caching.Distributed;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Shared.Contracts.CQRS;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.MultiTenancy;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Notification.Channels;
global using Notification.Channels.Email;
global using Notification.Channels.WhatsApp;
global using Notification.Contracts;
global using Notification.Contracts.Email;
global using Notification.Contracts.Email.Models;
global using Notification.Contracts.Enums;
global using Notification.Dispatch;
global using Notification.Options;
global using Notification.Persistence;
global using Notification.Persistence.Configurations;
global using Notification.Persistence.Entities;
global using Notification.Verification;

namespace Notification;

public static class NotificationRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
