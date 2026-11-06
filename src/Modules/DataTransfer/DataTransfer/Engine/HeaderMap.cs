using System.Globalization;
using System.Text;

namespace DataTransfer.Engine;

/// <summary>
/// Maps the header row of a file to definition columns. A header matches a column by its machine name or by either
/// localized header, ignoring case, surrounding/inner whitespace differences, a BOM and a trailing <c>*</c> (the
/// "required" marker written to templates). Unknown headers are ignored; a duplicate or a missing required column
/// fails the whole file before any row is read.
/// </summary>
public sealed class HeaderMap
{
    private readonly (int Index, string Column)[] _mapped;

    private HeaderMap((int Index, string Column)[] mapped, IReadOnlyList<string> ignored)
    {
        _mapped = mapped;
        IgnoredHeaders = ignored;
    }

    /// <summary>Headers that matched no column (reported to the user, not an error).</summary>
    public IReadOnlyList<string> IgnoredHeaders { get; }

    public IEnumerable<(int Index, string Column)> Columns => _mapped;

    public static HeaderMap Build(IReadOnlyList<string?> header, IReadOnlyList<TransferColumn> columns)
    {
        var lookup = BuildLookup(columns);
        var mapped = new List<(int, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ignored = new List<string>();

        for (var i = 0; i < header.Count; i++)
        {
            var text = header[i];
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (!lookup.TryGetValue(Normalize(text), out var column))
            {
                ignored.Add(text.Trim());
                continue;
            }

            if (!seen.Add(column))
                throw new InvalidTransferFileException("duplicate_column", column);
            mapped.Add((i, column));
        }

        var missing = columns.Where(c => c.Required && !seen.Contains(c.Name)).Select(c => c.HeaderFor(CultureInfo.CurrentUICulture.Name)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidTransferFileException("missing_columns", string.Join(", ", missing));
        }

        if (mapped.Count == 0)
            throw new InvalidTransferFileException("no_known_columns");

        return new HeaderMap([.. mapped], ignored);
    }

    private static Dictionary<string, string> BuildLookup(IReadOnlyList<TransferColumn> columns)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            foreach (var alias in new[] { column.Name, column.Header.En, column.Header.Ar })
                lookup.TryAdd(Normalize(alias), column.Name);
        }

        return lookup;
    }

    /// <summary>Canonical header form: no BOM, trimmed, single spaces, no trailing required marker, lower-case.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text.Trim().TrimStart('﻿').TrimEnd('*').Trim())
        {
            if (char.IsWhiteSpace(ch) || ch == '_')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
