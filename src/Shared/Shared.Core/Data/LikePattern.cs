namespace Shared.Core.Data;

/// <summary>
/// Builds safe PostgreSQL <c>ILIKE</c> patterns from user input. The LIKE wildcards in the input are escaped, so
/// searching "50%" matches the text "50%" instead of everything, and "a_b" does not match "axb". Always pass
/// <see cref="EscapeCharacter"/> as the third argument:
/// <c>EF.Functions.ILike(column, LikePattern.Contains(term), LikePattern.EscapeCharacter)</c>.
/// </summary>
public static class LikePattern
{
    /// <summary>The escape character the patterns use; pass it to <c>EF.Functions.ILike</c>.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>A "contains" pattern: the trimmed, escaped term between two <c>%</c> wildcards.</summary>
    public static string Contains(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return $"%{Escape(term.Trim())}%";
    }

    /// <summary>Escapes the escape character itself, <c>%</c> and <c>_</c>, so they match literally.</summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
    }
}
