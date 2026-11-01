using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Centralised, idempotent QuestPDF initialisation. Call once at application startup.
/// Disables environment fonts for deterministic rendering across Windows/Linux and
/// registers an Arabic-capable fallback font (Noto Sans Arabic) so Arabic company
/// names render real glyphs instead of placeholder boxes.
/// </summary>
public static class PdfConfiguration
{
    /// <summary>Font family name used as the Arabic fallback in invoice documents.</summary>
    public const string ArabicFontFamily = "Noto Sans Arabic";

    private static bool _configured;
    private static readonly object Gate = new();

    public static void Configure()
    {
        if (_configured) return;

        lock (Gate)
        {
            if (_configured) return;

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseEnvironmentFonts = false;
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

            FontManager.RegisterFontFromEmbeddedResource(
                "Subscriptions.Resources.Fonts.NotoSansArabic-Regular.ttf");

            _configured = true;
        }
    }
}
