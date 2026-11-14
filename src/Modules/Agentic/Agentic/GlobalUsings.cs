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
global using Shared.Core.MultiTenancy.Web;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using Agentic.Contracts.Agents;
global using Agentic.Contracts.Runs;
global using Agentic.Contracts.Tools;
global using Agentic.Data;
global using Agentic.Data.Repository;
global using Agentic.Domains.AgentRuns.Exceptions;
global using Agentic.Domains.AgentRuns.Models;
global using Agentic.Domains.Approvals.Exceptions;
global using Agentic.Domains.Approvals.Models;
global using Agentic.Domains.Knowledge.Models;
global using Agentic.Domains.Memory.Models;
global using Agentic.Domains.Policies.Models;
global using Agentic.Options;
global using Agentic.Resources;
global using Agentic.Runtime.Tools;

namespace Agentic;

/// <summary>Assembly marker for the Agentic module (mediator, Carter and Mapster scanning).</summary>
public static class AgenticRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
