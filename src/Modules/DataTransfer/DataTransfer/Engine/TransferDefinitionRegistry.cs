namespace DataTransfer.Engine;

/// <summary>
/// Resolves the definitions the owning modules registered (scoped: definitions inject their module's repositories).
/// Keys are case-insensitive and must be unique per direction; a duplicate is a wiring bug and throws.
/// </summary>
public sealed class TransferDefinitionRegistry(
    IEnumerable<IImportDefinition> imports,
    IEnumerable<IExportDefinition> exports)
{
    private readonly Dictionary<string, IImportDefinition> _imports = Index(imports);
    private readonly Dictionary<string, IExportDefinition> _exports = Index(exports);

    public IReadOnlyCollection<IImportDefinition> Imports => _imports.Values;

    public IReadOnlyCollection<IExportDefinition> Exports => _exports.Values;

    public IImportDefinition GetImport(string key) =>
        _imports.TryGetValue(key, out var definition) ? definition : throw new TransferDefinitionNotFoundException(key);

    public IExportDefinition GetExport(string key) =>
        _exports.TryGetValue(key, out var definition) ? definition : throw new TransferDefinitionNotFoundException(key);

    /// <summary>Import or export definition with this key (columns are the same for both directions).</summary>
    public ITransferDefinition Get(string key) =>
        _imports.TryGetValue(key, out var import) ? import
        : _exports.TryGetValue(key, out var export) ? export
        : throw new TransferDefinitionNotFoundException(key);

    private static Dictionary<string, T> Index<T>(IEnumerable<T> definitions) where T : ITransferDefinition
    {
        var index = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (!index.TryAdd(definition.Key, definition))
                throw new InvalidOperationException($"Two {typeof(T).Name}s use the key '{definition.Key}'.");
        }

        return index;
    }
}
