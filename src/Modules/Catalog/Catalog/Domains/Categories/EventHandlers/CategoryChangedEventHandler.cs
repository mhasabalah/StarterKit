using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Categories.Events;

namespace Catalog.Domains.Categories.EventHandlers;

/// <summary>Dispatched after the category change committed, so the next read rebuilds a fresh tree.</summary>
public class CategoryChangedEventHandler(ICategoryTreeCache cache) : INotificationHandler<CategoryChangedEvent>
{
    public Task Handle(CategoryChangedEvent notification, CancellationToken cancellationToken) =>
        cache.InvalidateAsync(cancellationToken);
}
