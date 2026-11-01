using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Enums;
using Shared.Core.Attributes;

namespace Shared.Core.Enums;

/// <summary>
/// Hybrid enum lookup service that:
/// 1. Uses reflection for simple enums (generic approach)
/// 2. Uses custom handlers for complex enums (flexible approach)
/// 3. Caches results for performance
/// </summary>
public class HybridEnumLookupService : IEnumLookupService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<HybridEnumLookupService> _logger;
    private readonly Dictionary<string, Type> _enumRegistry;
    private readonly Dictionary<string, IEnumHandler> _customHandlers;
    private const int CacheDurationHours = 24;

    public HybridEnumLookupService(
        IMemoryCache cache,
        ILogger<HybridEnumLookupService> logger,
        IEnumerable<IEnumHandler> customHandlers,
        params Assembly[] assemblies)
    {
        _cache = cache;
        _logger = logger;
        _enumRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        _customHandlers = customHandlers.ToDictionary(
            h => NormalizeEnumName(h.EnumName),
            h => h,
            StringComparer.OrdinalIgnoreCase);

        RegisterEnumsFromAssemblies(assemblies);

        _logger.LogInformation(
            "Hybrid Enum Lookup initialized: {EnumCount} enums, {HandlerCount} custom handlers",
            _enumRegistry.Count,
            _customHandlers.Count);
    }

    /// <inheritdoc />
    public EnumResponseDto GetEnumValues(string enumName)
    {
        return GetEnumValuesAsync(enumName, new EnumLookupContext()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get enum values with optional context (for custom handlers).
    /// </summary>
    public async Task<EnumResponseDto> GetEnumValuesAsync(string enumName, EnumLookupContext context)
    {
        if (string.IsNullOrWhiteSpace(enumName))
        {
            throw new ArgumentException("Enum name cannot be null or empty.", nameof(enumName));
        }

        var normalizedName = NormalizeEnumName(enumName);

        // Check if custom handler exists (takes priority)
        if (_customHandlers.TryGetValue(normalizedName, out var handler))
        {
            _logger.LogDebug("Using custom handler for enum '{EnumName}'", enumName);
            return await handler.HandleAsync(context);
        }

        // Use generic reflection-based approach with caching
        var cacheKey = $"Enum_{normalizedName}";
        return _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(CacheDurationHours);

            if (!_enumRegistry.TryGetValue(normalizedName, out var enumType))
            {
                _logger.LogWarning("Enum '{EnumName}' not found. Available: {AvailableEnums}",
                    enumName, string.Join(", ", _enumRegistry.Keys));
                throw new ArgumentException(
                    $"Enum '{enumName}' not found. Use GET /api/v1/enums to see available enums.",
                    nameof(enumName));
            }

            var items = Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(enumValue => CreateEnumItem(enumValue, enumType))
                .OrderBy(x => x.Id)
                .ToList();

            _logger.LogInformation("Loaded {Count} values for enum '{EnumName}' (generic)",
                items.Count, enumType.Name);

            return new EnumResponseDto
            {
                EnumName = enumType.Name,
                Count = items.Count,
                Items = items
            };
        })!;
    }

    /// <inheritdoc />
    public List<string> GetAllRegisteredEnums()
    {
        var genericEnums = _enumRegistry.Values.Select(t => t.Name);
        var customEnums = _customHandlers.Values.Select(h => h.EnumName);

        return genericEnums
            .Concat(customEnums)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

    /// <summary>
    /// Checks if a custom handler exists for the given enum.
    /// </summary>
    public bool HasCustomHandler(string enumName)
    {
        var normalizedName = NormalizeEnumName(enumName);
        return _customHandlers.ContainsKey(normalizedName);
    }

    private void RegisterEnumsFromAssemblies(Assembly[] assemblies)
    {
        var enumCount = 0;

        foreach (var assembly in assemblies)
        {
            try
            {
                var enumTypes = assembly.GetTypes()
                    .Where(t => t.IsEnum && t.IsPublic)
                    .ToList();

                foreach (var enumType in enumTypes)
                {
                    var normalizedName = NormalizeEnumName(enumType.Name);

                    // Skip if custom handler exists (handler takes priority)
                    if (_customHandlers.ContainsKey(normalizedName))
                    {
                        _logger.LogDebug("Skipping enum '{EnumName}' - custom handler exists",
                            enumType.Name);
                        continue;
                    }

                    if (!_enumRegistry.ContainsKey(normalizedName))
                    {
                        _enumRegistry[normalizedName] = enumType;
                        enumCount++;
                    }
                }

                _logger.LogInformation("Registered {Count} enums from '{Assembly}'",
                    enumTypes.Count, assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan assembly '{Assembly}'",
                    assembly.GetName().Name);
            }
        }

        _logger.LogInformation("Total enums registered: {Count}", enumCount);
    }

    private EnumItemDto CreateEnumItem(Enum enumValue, Type enumType)
    {
        var memberName = enumValue.ToString();
        var field = enumType.GetField(memberName);
        var attribute = field?.GetCustomAttribute<LocalizedDescriptionAttribute>();

        return new EnumItemDto
        {
            Id = Convert.ToInt32(enumValue),
            Key = memberName,
            NameEn = attribute?.DescriptionEn ?? memberName,
            NameAr = attribute?.DescriptionAr ?? memberName
        };
    }

    private static string NormalizeEnumName(string enumName)
    {
        return enumName.Replace("-", "")
                       .Replace("_", "")
                       .Replace(" ", "")
                       .ToLowerInvariant();
    }
}
