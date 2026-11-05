using System.Collections.Concurrent;
using MassTransit;
using Shared.Contracts.Messaging;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// Stable wire names of integration events: <c>[MessageUrn("module.event.v1")]</c> ⇄ CLR type. The outbox stores the
/// urn (never the CLR type name), so classes can be renamed or moved without breaking rows already written.
/// </summary>
public static class IntegrationEventTypes
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();
    private static readonly Lock Gate = new();
    private static volatile IReadOnlyDictionary<string, Type> _byName = new Dictionary<string, Type>();
    private static bool _referencesLoaded;

    /// <summary>The urn of <paramref name="type"/>. Throws when the attribute is missing (every event must pin its name).</summary>
    public static string NameOf(Type type) => Names.GetOrAdd(type, static t =>
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(t))
            throw new InvalidOperationException($"{t.FullName} is not an {nameof(IIntegrationEvent)}.");

        var attribute = t.GetCustomAttribute<MessageUrnAttribute>(inherit: false)
                        ?? throw new InvalidOperationException(
                            $"Integration event {t.FullName} has no [MessageUrn(\"<module>.<event>.v1\")]: its wire name " +
                            "must be pinned so renaming or moving the class never breaks queued/outboxed messages.");

        return attribute.Urn.ToString();
    });

    /// <summary>The CLR type of a stored urn, or <c>null</c> when no loaded integration event declares it.</summary>
    public static Type? Resolve(string name)
    {
        if (_byName.TryGetValue(name, out var type))
            return type;

        lock (Gate)
        {
            if (_byName.TryGetValue(name, out type))
                return type;

            // Contracts assemblies load lazily: make sure everything the application references is loaded once.
            if (!_referencesLoaded)
            {
                LoadReferencedAssemblies(Assembly.GetEntryAssembly());
                _referencesLoaded = true;
            }

            _byName = Scan();
            return _byName.GetValueOrDefault(name);
        }
    }

    /// <summary>Every loaded integration event type by urn. Duplicate urns are a programming error.</summary>
    public static IReadOnlyDictionary<string, Type> Scan(IEnumerable<Assembly>? assemblies = null)
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).Where(a => !a.IsDynamic)
                     .SelectMany(SafeTypes)
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t)))
        {
            if (type.GetCustomAttribute<MessageUrnAttribute>(inherit: false) is null)
                continue;

            var name = NameOf(type);
            if (map.TryGetValue(name, out var existing) && existing != type)
                throw new InvalidOperationException(
                    $"Integration events {existing.FullName} and {type.FullName} share the urn '{name}'.");
            map[name] = type;
        }

        return map;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static void LoadReferencedAssemblies(Assembly? root)
    {
        if (root is null)
            return;

        var seen = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName!));
        var queue = new Queue<Assembly>([root]);
        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.FullName) || IsFramework(reference))
                    continue;
                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // Optional/unavailable reference: nothing to scan there.
                }
            }
        }
    }

    private static bool IsFramework(AssemblyName name) =>
        name.Name is { } n && (n.StartsWith("System", StringComparison.Ordinal) ||
                               n.StartsWith("Microsoft", StringComparison.Ordinal) ||
                               n.Equals("netstandard", StringComparison.Ordinal) ||
                               n.Equals("mscorlib", StringComparison.Ordinal));
}
