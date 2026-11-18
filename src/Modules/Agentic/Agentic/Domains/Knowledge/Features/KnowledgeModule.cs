using Agentic.Contracts.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Knowledge.Features;

/// <summary>
/// Operators' view of the tenant's knowledge (<c>/api/v1/knowledge-documents</c>). Documents are indexed by the modules
/// that own them (<c>IndexKnowledgeDocumentCommand</c>); there is no upload endpoint here. See wiki/Modules/Agentic.md.
/// </summary>
public class KnowledgeModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet("Knowledge").Build();

        app.MapGroup(EndpointConfig.BaseApiPath + "/knowledge-documents")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled)
            .MapGet("/", async ([AsParameters] GetKnowledgeDocumentsRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetKnowledgeDocumentsQuery(request.SourceKey,
                    request.Status, request.PageNumber ?? 1, request.PageSize ?? 20), ct))))
            .WithName("GetKnowledgeDocuments").Produces<ApiResponse<PaginatedResult<KnowledgeDocumentDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Knowledge documents")
            .WithDescription(
                "The tenant's indexed documents with their status, revision, passage count and embedding model. " +
                "Content is not returned.")
            .RequireTenantPermission(TenantPermissions.AgentsManage);
    }

    private static async ValueTask<object?> RequireEnabled(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AgenticOptions>>().Value.Enabled)
            throw new AgenticDisabledException();
        return await next(context);
    }
}

public sealed record GetKnowledgeDocumentsRequest(
    string? SourceKey,
    KnowledgeDocumentStatus? Status,
    int? PageNumber,
    int? PageSize);
