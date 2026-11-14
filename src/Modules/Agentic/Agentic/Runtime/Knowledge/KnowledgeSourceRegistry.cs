using System.Text.RegularExpressions;
using Agentic.Contracts.Knowledge;

namespace Agentic.Runtime.Knowledge;

/// <summary>
/// The knowledge sources the modules registered (<c>AddKnowledgeSource</c>), validated once at startup like the agent
/// registry: a duplicate key or a source without an explicit access decision stops the host.
/// </summary>
public sealed partial class KnowledgeSourceRegistry
{
    private readonly Dictionary<string, KnowledgeSourceDefinition> _sources = new(StringComparer.Ordinal);

    public KnowledgeSourceRegistry(IEnumerable<KnowledgeSourceDefinition> sources)
    {
        foreach (var source in sources)
        {
            if (!KeyRegex().IsMatch(source.Key) || source.Key.Length > Domains.Knowledge.Models.KnowledgeDocument.SourceKeyMaxLength)
            {
                throw new InvalidOperationException(
                    $"Knowledge source key '{source.Key}' must look like 'module.content' (lower case letters, digits, '_' and '-').");
            }

            if (string.IsNullOrWhiteSpace(source.RequiredPermission) == !source.AllowAnyMember)
            {
                throw new InvalidOperationException(
                    $"Knowledge source '{source.Key}' must declare exactly one of RequiredPermission or AllowAnyMember = true.");
            }

            if (!_sources.TryAdd(source.Key, source))
                throw new InvalidOperationException($"Two knowledge sources use the key '{source.Key}'.");
        }
    }

    public IReadOnlyCollection<KnowledgeSourceDefinition> Sources => _sources.Values;

    public KnowledgeSourceDefinition? Find(string key) => _sources.GetValueOrDefault(key);

    /// <summary>The sources a user with <paramref name="permissions"/> may read.</summary>
    public IReadOnlyList<string> ReadableBy(IReadOnlySet<string> permissions) =>
        _sources.Values
            .Where(s => s.AllowAnyMember || permissions.Contains(s.RequiredPermission!))
            .Select(s => s.Key)
            .ToList();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9_-]*)+$")]
    private static partial Regex KeyRegex();
}
