global using System.Reflection;
global using Carter;
global using FluentValidation;
global using Mapster;
global using MassTransit;
global using MediatR;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Logging;
global using Shared.Core.Data;
global using Shared.Core.Data.Seed;
global using Shared.Core.DDD;
global using Shared.Core.Exceptions;
global using Shared.Core.Web;
global using Shared.Contracts.CQRS;
global using Attachment.Data;
global using Attachment.Data.Repository.Attachments;
global using Attachment.Domains.Attachments.Models;
global using Attachment.Contracts.Attachments.Dtos;

namespace Attachment;

public static class AttachmentRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
