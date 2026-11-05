using System.Text.RegularExpressions;

namespace Notification.Channels.Email;

/// <summary>
/// Brand overrides for the email design system (section <c>Email:Theme</c>). Everything else is a fixed, accessible
/// token. Colors are 6-digit hex (<c>#1a1a2e</c>); invalid values fall back to the defaults (never break mail).
/// </summary>
public sealed class EmailThemeOptions
{
    /// <summary>Header background and primary buttons. Default: deep navy.</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Text on <see cref="PrimaryColor"/>. Default: white. Keep WCAG AA contrast (4.5:1).</summary>
    public string? OnPrimaryColor { get; set; }

    /// <summary>Links and accents inside the body.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Absolute HTTPS URL of a logo (PNG, ~2x of 140×32). When empty the brand name is shown as text.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Logo width in px (height scales). Default 140.</summary>
    public int LogoWidth { get; set; } = 140;

    /// <summary>Optional links in the footer.</summary>
    public string? HelpCenterUrl { get; set; }

    public string? PrivacyUrl { get; set; }

    /// <summary>Postal address line in the footer (CAN-SPAM / GDPR transparency for marketing-adjacent mail).</summary>
    public string? CompanyAddress { get; set; }
}

/// <summary>
/// The email design system's tokens: the ONLY place colors, type sizes, spacing and radii are defined. Templates and
/// components read them as <c>{{ t.&lt;token&gt; }}</c>; a test fails when a template hard-codes a hex color.
/// Semantic names (text, textMuted, danger, ...) describe purpose, not appearance, so a rebrand or dark-mode tweak
/// never touches a template.
/// </summary>
public static partial class EmailDesignTokens
{
    /// <summary>Light-scheme palette. Dark-scheme overrides live in the layout's <c>prefers-color-scheme</c> block.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        // surfaces
        ["bg"] = "#f1f5f9",
        ["surface"] = "#ffffff",
        ["surfaceMuted"] = "#f8fafc",
        ["border"] = "#e2e8f0",

        // text (contrast vs surface: text 17.9:1, textBody 10.3:1, textSecondary 7.6:1, textMuted 4.8:1)
        ["text"] = "#0f172a",
        ["textStrong"] = "#1e293b",
        ["textBody"] = "#374151",
        ["textSecondary"] = "#4b5563",
        ["textMuted"] = "#64748b",
        ["textSubtle"] = "#94a3b8",
        ["textNeutral"] = "#334155",

        // brand
        ["brand"] = "#1a1a2e",
        ["onBrand"] = "#ffffff",
        ["accent"] = "#2563eb",

        // feedback
        ["info"] = "#2563eb", ["infoBg"] = "#eff6ff", ["infoBorder"] = "#bfdbfe", ["infoText"] = "#1e40af",
        ["success"] = "#16a34a", ["successStrong"] = "#059669", ["successBg"] = "#f0fdf4",
        ["successBorder"] = "#bbf7d0", ["successText"] = "#15803d",
        ["warning"] = "#d97706", ["warningStrong"] = "#b45309", ["warningBg"] = "#fffbeb",
        ["warningBorder"] = "#fde68a", ["warningText"] = "#92400e",
        ["danger"] = "#dc2626", ["dangerBg"] = "#fef2f2", ["dangerBorder"] = "#fecaca", ["dangerText"] = "#991b1b",

        // dark scheme (applied by the layout under prefers-color-scheme: dark / Outlook.com data-ogsc)
        ["darkBg"] = "#0b1120",
        ["darkSurface"] = "#111827",
        ["darkSurfaceMuted"] = "#1f2937",
        ["darkBorder"] = "#334155",
        ["darkText"] = "#f8fafc",
        ["darkTextBody"] = "#cbd5e1",
        ["darkTextMuted"] = "#94a3b8",

        // type scale (px) and rhythm
        ["fontSizeXs"] = "12px",
        ["fontSizeSm"] = "14px",
        ["fontSizeBase"] = "15px",
        ["fontSizeLg"] = "18px",
        ["fontSizeXl"] = "22px",
        ["fontSizeCode"] = "30px",
        ["lineHeight"] = "1.7",
        ["space1"] = "4px", ["space2"] = "8px", ["space3"] = "12px", ["space4"] = "16px",
        ["space5"] = "20px", ["space6"] = "24px", ["space8"] = "32px", ["space10"] = "40px",
        ["radius"] = "8px",
        ["radiusLg"] = "12px",
        ["fontFamily"] = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif",
        ["fontFamilyAr"] = "'Tahoma', 'Segoe UI', Arial, sans-serif",
        ["fontFamilyMono"] = "'SFMono-Regular', Menlo, Consolas, 'Liberation Mono', monospace"
    };

    /// <summary>Defaults merged with the validated brand overrides.</summary>
    public static Dictionary<string, object> Build(EmailThemeOptions? theme)
    {
        var tokens = Defaults.ToDictionary(p => p.Key, p => (object)p.Value, StringComparer.Ordinal);
        if (theme is null)
            return tokens;

        Override(tokens, "brand", theme.PrimaryColor);
        Override(tokens, "onBrand", theme.OnPrimaryColor);
        Override(tokens, "accent", theme.AccentColor);
        return tokens;
    }

    public static bool IsHexColor(string? value) => value is not null && HexColor().IsMatch(value);

    private static void Override(Dictionary<string, object> tokens, string key, string? value)
    {
        if (IsHexColor(value))
            tokens[key] = value!.ToLowerInvariant();
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
