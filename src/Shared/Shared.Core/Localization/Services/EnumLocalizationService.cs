using System.Globalization;
using System.Reflection;
using Shared.Contracts.Attributes;

namespace Shared.Core.Localization.Services;

/// <summary>
/// Reads <see cref="LocalizedDescriptionAttribute"/> via reflection and returns
/// the name that matches the current request culture (ar / en).
/// Results are cached per-type in a static dictionary — reflection happens once per enum type.
/// </summary>
public class EnumLocalizationService : IEnumLocalizationService
{
    // Instance-level cache (the class is registered as Singleton so there is one
    // instance per DI container — instance field avoids bleed between test hosts).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type,
            Dictionary<string, LocalizedDescriptionAttribute?>>
        _cache = new();

    public string Localize<TEnum>(TEnum value) where TEnum : Enum
    {
        var memberName = value.ToString()!;
        var map = _cache.GetOrAdd(typeof(TEnum), BuildMap);

        map.TryGetValue(memberName, out var attr);

        return attr is null ? memberName : SelectDescription(attr);
    }

    // Separated from Localize so culture detection logic lives in one place
    // and can be extended without touching the public API.
    private static string SelectDescription(LocalizedDescriptionAttribute attr)
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ar" => attr.DescriptionAr,
            _ => attr.DescriptionEn // default: English
        };
    }

    private static Dictionary<string, LocalizedDescriptionAttribute?> BuildMap(Type enumType)
    {
        return Enum.GetNames(enumType)
            .ToDictionary(
                name => name,
                name => enumType.GetField(name)
                    ?.GetCustomAttribute<LocalizedDescriptionAttribute>());
    }
}
