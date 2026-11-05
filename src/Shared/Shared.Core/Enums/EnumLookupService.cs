using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Enums;
using Shared.Contracts.Attributes;

namespace Shared.Core.Enums;

/// <summary>
/// Generic reflection-based service for enum lookup with bilingual localization.
/// Scans registered assemblies for enum types and caches their values with translations.
/// </summary>
public class EnumLookupService : IEnumLookupService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<EnumLookupService> _logger;
    private readonly Dictionary<string, Type> _enumRegistry;
    private const int CacheDurationHours = 24;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnumLookupService"/> class.
    /// </summary>
    /// <param name="cache">Memory cache for storing enum lookup results.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="assemblies">Assemblies to scan for enum types.</param>
    public EnumLookupService(IMemoryCache cache, ILogger<EnumLookupService> logger, params Assembly[] assemblies)
    {
        _cache = cache;
        _logger = logger;
        _enumRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        RegisterEnumsFromAssemblies(assemblies);
    }

    /// <inheritdoc />
    public EnumResponseDto GetEnumValues(string enumName)
    {
        if (string.IsNullOrWhiteSpace(enumName))
        {
            throw new ArgumentException("Enum name cannot be null or empty.", nameof(enumName));
        }

        var normalizedName = NormalizeEnumName(enumName);
        var cacheKey = $"Enum_{normalizedName}";

        return _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(CacheDurationHours);

            if (!_enumRegistry.TryGetValue(normalizedName, out var enumType))
            {
                _logger.LogWarning("Enum '{EnumName}' not found in registry. Available enums: {AvailableEnums}",
                    enumName, string.Join(", ", _enumRegistry.Keys));
                throw new ArgumentException($"Enum '{enumName}' not found. Use GET /api/v1/enums to see available enums.", nameof(enumName));
            }

            var items = Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(enumValue => CreateEnumItem(enumValue, enumType))
                .OrderBy(x => x.Id)
                .ToList();

            _logger.LogInformation("Loaded {Count} values for enum '{EnumName}' into cache", items.Count, enumType.Name);

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
        return _enumRegistry.Values
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

    /// <summary>
    /// Scans assemblies for public enum types and registers them.
    /// </summary>
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

                    if (!_enumRegistry.ContainsKey(normalizedName))
                    {
                        _enumRegistry[normalizedName] = enumType;
                        enumCount++;
                    }
                }

                _logger.LogInformation("Registered {Count} enums from assembly '{AssemblyName}'",
                    enumTypes.Count, assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan assembly '{AssemblyName}' for enums", assembly.GetName().Name);
            }
        }

        _logger.LogInformation("Total enums registered: {Count}", enumCount);
    }

    /// <summary>
    /// Creates an EnumItemDto from an enum value by reading its LocalizedDescription attribute.
    /// </summary>
    private static EnumItemDto CreateEnumItem(Enum enumValue, Type enumType)
    {
        var memberName = enumValue.ToString();
        var field = enumType.GetField(memberName);
        var attribute = field?.GetCustomAttribute<LocalizedDescriptionAttribute>();

        return new EnumItemDto
        {
            Id = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture),
            Key = memberName,
            NameEn = attribute?.DescriptionEn ?? memberName,
            NameAr = attribute?.DescriptionAr ?? memberName
        };
    }

    /// <summary>
    /// Normalizes enum name for case-insensitive lookup.
    /// Removes hyphens, underscores, and spaces (e.g., "attachment-type" -> "attachmenttype").
    /// </summary>
    private static string NormalizeEnumName(string enumName)
    {
        return enumName.Replace("-", "")
                       .Replace("_", "")
                       .Replace(" ", "")
                       .ToLowerInvariant();
    }
}
