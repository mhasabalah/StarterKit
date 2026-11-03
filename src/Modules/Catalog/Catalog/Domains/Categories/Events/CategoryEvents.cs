namespace Catalog.Domains.Categories.Events;

/// <summary>Any create/update/(de)activation of a category. Handled by the tree-cache invalidation.</summary>
public record CategoryChangedEvent(Guid CategoryId) : IDomainEvent;
