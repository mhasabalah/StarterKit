using System.Text.Json;
using Shared.Core.Auditing;
using Shared.Core.MultiTenancy.Data;
using Shared.Core.Pagination;

namespace Catalog.Domains.Products.Features.GetProductHistory;

public class GetProductHistoryQueryValidator : AbstractValidator<GetProductHistoryQuery>
{
    public GetProductHistoryQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        Include(new BaseQueryValidator<GetProductHistoryQuery>());
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>
/// Reads the product's <see cref="TenantAuditEntry"/> rows (the tenant filter keeps other tenants' trails invisible).
/// The product itself must be visible to the tenant (soft-deleted included), otherwise 404.
/// </summary>
public class GetProductHistoryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetProductHistoryQuery, PaginatedResult<ProductHistoryEntryDto>>
{
    private static readonly string ProductType = nameof(Product);

    public async Task<PaginatedResult<ProductHistoryEntryDto>> Handle(
        GetProductHistoryQuery query, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Products.IgnoreNonIsolationFilters().AsNoTracking()
            .AnyAsync(p => p.Id == query.ProductId, cancellationToken);
        if (!exists)
            throw new ProductNotFoundException(query.ProductId);

        var entityId = query.ProductId.ToString();
        var page = await dbContext.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == ProductType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new Row(a.Id, a.Action, a.ChangesJson, a.ActorUserId, a.ImpersonatorUserId, a.CorrelationId,
                a.IpAddress, a.OccurredAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        return page.Map(r => new ProductHistoryEntryDto(r.Id, r.Action.ToString(), ParseChanges(r.ChangesJson),
            r.ActorUserId, r.ImpersonatorUserId, r.CorrelationId, r.IpAddress, r.OccurredAt));
    }

    private static JsonElement ParseChanges(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record Row(Guid Id, AuditAction Action, string ChangesJson, Guid? ActorUserId,
        Guid? ImpersonatorUserId, string? CorrelationId, string? IpAddress, DateTime OccurredAt);
}
