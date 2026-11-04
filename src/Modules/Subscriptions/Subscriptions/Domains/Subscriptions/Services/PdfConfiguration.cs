using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Centralised, idempotent QuestPDF initialisation. Call once at application startup.
/// Disables environment fonts for deterministic rendering across Windows/Linux and
/// registers an Arabic-capable fallback font (Noto Sans Arabic) so Arabic company
/// names render real glyphs instead of placeholder boxes.
/// <para>
/// The QuestPDF license is CONFIGURATION, not code (<c>Subscriptions:Pdf:License</c>): <c>Community</c> is free only
/// below USD 1M annual gross revenue (and not for public-sector or publicly traded companies); larger products set
/// <c>Professional</c> / <c>Enterprise</c> after buying it. See wiki/Reference/Third-Party-Licenses.md.
/// </para>
/// </summary>
public static class PdfConfiguration
{
    /// <summary>Font family name used as the Arabic fallback in invoice documents.</summary>
    public const string ArabicFontFamily = "Noto Sans Arabic";

    private static bool _configured;
    private static readonly object Gate = new();

    /// <param name="license">QuestPDF license type name (Community, Professional, Enterprise); null = Community.</param>
    public static void Configure(string? license = null)
    {
        var licenseType = ParseLicense(license); // validated even when already configured: a typo fails fast
        if (_configured)
            return;

        lock (Gate)
        {
            if (_configured)
                return;

            QuestPDF.Settings.License = licenseType;
            QuestPDF.Settings.UseSystemFonts = false;
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

            FontManager.RegisterFontFromEmbeddedResource(
                "Subscriptions.Resources.Fonts.NotoSansArabic-Regular.ttf");

            _configured = true;
        }
    }

    public static LicenseType ParseLicense(string? license) =>
        string.IsNullOrWhiteSpace(license)
            ? LicenseType.Community
            : Enum.TryParse<LicenseType>(license, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Subscriptions:Pdf:License '{license}' is not a QuestPDF license type (Community, Professional, Enterprise).");
}
