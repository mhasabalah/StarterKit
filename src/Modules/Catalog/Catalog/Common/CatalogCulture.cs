using System.Globalization;

namespace Catalog.Common;

/// <summary>
/// Request-culture helpers. Projections capture <see cref="IsArabic"/> once per query as a parameter
/// (<c>arabic ? p.Name.Ar : p.Name.En</c>), so the localized column is chosen in SQL.
/// </summary>
internal static class CatalogCulture
{
    public static bool IsArabic() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

    public static string Pick(bool arabic, string en, string ar) => arabic ? ar : en;
}
