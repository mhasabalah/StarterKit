using System.Globalization;
using System.Text;

namespace DataTransfer.Engine;

/// <summary>Cell text rules shared by readers and writers.</summary>
public static class CellText
{
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// CSV/formula-injection guard (OWASP): a text cell that a spreadsheet would run as a formula gets a leading
    /// apostrophe. Plain numbers (<c>-5</c>, <c>+3.2</c>) are left alone so numeric columns stay numeric.
    /// </summary>
    public static string? EscapeFormula(string? value)
    {
        if (string.IsNullOrEmpty(value) || Array.IndexOf(FormulaTriggers, value[0]) < 0)
            return value;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return value;
        return "'" + value;
    }

    /// <summary>
    /// Import side of <see cref="EscapeFormula"/>: trims, turns blank into null and removes the guard apostrophe, so an
    /// exported file re-imports unchanged.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;
        if (trimmed.Length > 1 && trimmed[0] == '\'' && Array.IndexOf(FormulaTriggers, trimmed[1]) >= 0)
            return trimmed[1..];
        return trimmed;
    }

    /// <summary>Replaces characters XML 1.0 cannot carry (control characters, lone surrogates) with U+FFFD.</summary>
    public static string ToXmlSafe(string value)
    {
        if (IsXmlSafe(value))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(ch).Append(value[++i]);
                continue;
            }

            builder.Append(IsXmlChar(ch) ? ch : '�');
        }

        return builder.ToString();
    }

    private static bool IsXmlSafe(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (!IsXmlChar(ch))
                return false;
        }

        return true;
    }

    private static bool IsXmlChar(char ch) =>
        ch is '\t' or '\n' or '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD);
}
