using Microsoft.Extensions.DependencyInjection;

namespace Search.Contracts.Indexing;

/// <summary>How an owning module plugs an index into Search (ADR-0036).</summary>
public static class SearchIndexServiceCollectionExtensions
{
    /// <summary>
    /// Registers <paramref name="definition"/> and the source Search reads its documents from. Keep the entity in sync
    /// by raising <see cref="IntegrationEvents.SearchDocumentsChangedIntegrationEvent"/> from the aggregate
    /// (<c>AddIntegrationEvent</c>) whenever a searchable value changes. The definition is validated when the host
    /// starts; an invalid or duplicate one stops it.
    /// </summary>
    public static IServiceCollection AddSearchIndex<TSource>(this IServiceCollection services, SearchIndexDefinition definition)
        where TSource : class, ISearchDocumentSource
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Key);

        services.AddSingleton(definition);
        services.AddKeyedScoped<ISearchDocumentSource, TSource>(definition.Key);
        return services;
    }
}
