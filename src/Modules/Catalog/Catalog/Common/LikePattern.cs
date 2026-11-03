namespace Catalog.Common;

/// <summary>
/// Builds safe ILIKE patterns: user input is escaped so '%' and '_' match literally
/// (searching "50%" must not match everything). Use with
/// <c>EF.Functions.ILike(column, LikePattern.Contains(term), LikePattern.EscapeCharacter)</c>.
/// </summary>
internal static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Contains(string term) => $"%{Escape(term.Trim())}%";

    public static string Escape(string value) =>
        value.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter)
            .Replace("%", EscapeCharacter + "%")
            .Replace("_", EscapeCharacter + "_");
}
