using Microsoft.Extensions.DependencyInjection;
using Search.Contracts.Indexing;
using Shared.Core.Modularity;

namespace Api.Tests.Architecture;

/// <summary>
/// Guards the search index registrations of the real host (ADR-0036): every index definition has its document source,
/// the source lives in a module's implementation, and the index key starts with that module's name
/// (<c>catalog.products</c> is Catalog's). The key is stored with every document, so it must say who owns it.
/// </summary>
public sealed class SearchIndexArchitectureTests
{
    private static readonly ModuleCatalog Modules = ModuleCatalog.FromHost(typeof(Program).Assembly);

    [Fact]
    public void Every_index_key_is_prefixed_by_the_module_that_owns_its_source()
    {
        var definitions = RealHost.Services.GetServices<SearchIndexDefinition>().ToList();
        var moduleAssemblies = Modules.Modules.ToDictionary(m => m.GetType().Assembly, m => m.Name);
        var violations = new List<string>();

        using var scope = RealHost.Services.CreateScope();
        foreach (var definition in definitions)
        {
            var source = scope.ServiceProvider.GetKeyedService<ISearchDocumentSource>(definition.Key);
            if (source is null)
            {
                violations.Add($"{definition.Key}: no document source (register it with AddSearchIndex<TSource>)");
                continue;
            }

            if (!moduleAssemblies.TryGetValue(source.GetType().Assembly, out var module))
            {
                violations.Add($"{definition.Key}: source {source.GetType().FullName} is not in a module implementation");
                continue;
            }

            var prefix = definition.Key.Split('.')[0];
            if (!string.Equals(prefix, module, StringComparison.OrdinalIgnoreCase))
                violations.Add($"{definition.Key}: owned by {module}, so the key must start with '{module.ToLowerInvariant()}.'");
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Index_keys_are_unique() =>
        RealHost.Services.GetServices<SearchIndexDefinition>().Select(d => d.Key).Should().OnlyHaveUniqueItems();
}
