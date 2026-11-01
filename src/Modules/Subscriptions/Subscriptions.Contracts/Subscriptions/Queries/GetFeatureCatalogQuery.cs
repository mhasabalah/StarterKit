using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetFeatureCatalogQuery(bool IncludeInactive = false) : IQuery<List<FeatureCatalogItemDto>>;
