namespace Shared.Core.Modularity;

/// <summary>
/// The host's modules, discovered from the <see cref="ModuleAssemblyAttribute"/>s of the host assembly and ordered by
/// (<see cref="IModule.Order"/>, <see cref="IModule.Name"/>). Fails fast on anything ambiguous: a referenced module
/// assembly without a manifest, a manifest that is not an <see cref="IModule"/>, or two modules with one name.
/// </summary>
public sealed class ModuleCatalog
{
    private ModuleCatalog(IReadOnlyList<IModule> modules) => Modules = modules;

    /// <summary>The modules in startup order.</summary>
    public IReadOnlyList<IModule> Modules { get; }

    /// <summary>Module names in startup order.</summary>
    public IReadOnlyList<string> Names => [.. Modules.Select(m => m.Name)];

    /// <summary>Consumer assemblies of every module, de-duplicated, in startup order.</summary>
    public Assembly[] ConsumerAssemblies => [.. Modules.SelectMany(m => m.ConsumerAssemblies).Distinct()];

    /// <summary>Enum assemblies of every module, de-duplicated, in startup order.</summary>
    public Assembly[] EnumAssemblies => [.. Modules.SelectMany(m => m.EnumAssemblies).Distinct()];

    /// <summary>Discovers the modules the <paramref name="hostAssembly"/> references.</summary>
    public static ModuleCatalog FromHost(Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);

        var names = hostAssembly.GetCustomAttributes<ModuleAssemblyAttribute>()
            .Select(a => a.AssemblyName)
            .ToList();

        if (names.Count == 0)
        {
            throw new InvalidOperationException(
                $"{hostAssembly.GetName().Name} references no modules: expected [assembly: ModuleAssembly(...)] " +
                "attributes generated from the module ProjectReferences (see src/Api/Api.csproj).");
        }

        return FromAssemblies(names.Select(n => Assembly.Load(new AssemblyName(n))));
    }

    /// <summary>Builds the catalog from module implementation assemblies.</summary>
    public static ModuleCatalog FromAssemblies(IEnumerable<Assembly> moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);

        var modules = moduleAssemblies.Distinct().Select(CreateManifest).ToList();

        var duplicates = modules.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate module name(s): {string.Join(", ", duplicates)}.");
        }

        return new ModuleCatalog(
        [
            .. modules.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal)
        ]);
    }

    private static IModule CreateManifest(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        var attribute = assembly.GetCustomAttribute<ModuleManifestAttribute>()
            ?? throw new InvalidOperationException(
                $"Module assembly '{assemblyName}' declares no manifest: add " +
                $"[assembly: ModuleManifest(typeof({assemblyName}ModuleManifest))] (ADR-0019).");

        var type = attribute.ManifestType;
        if (type.Assembly != assembly)
        {
            throw new InvalidOperationException(
                $"Module assembly '{assemblyName}' names manifest {type.FullName} from another assembly " +
                $"({type.Assembly.GetName().Name}); a module declares its own manifest.");
        }

        if (!typeof(IModule).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Module manifest {type.FullName} must be a concrete IModule with a public parameterless constructor.");
        }

        return (IModule)Activator.CreateInstance(type)!;
    }
}
