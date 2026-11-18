using System.Globalization;
using System.Resources;

namespace Agentic.Resources;

/// <summary>
/// Localized module text WITHOUT dependency injection (exceptions, aggregate invariants, the run executor), from the
/// same AgenticResource.resx / .ar.resx. Uses <see cref="CultureInfo.CurrentUICulture"/>: the request culture in a
/// handler, the initiating user's culture inside a job (the executor sets it for the whole attempt).
/// </summary>
public static class AgenticText
{
    private static readonly ResourceManager Resources =
        new("Agentic.Resources.AgenticResource", typeof(AgenticText).Assembly);

    public static string Get(string key, params object?[] args)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    /// <summary>The text for <paramref name="key"/>, or <paramref name="fallbackKey"/> when the first one is unknown.</summary>
    public static string GetOrDefault(string key, string fallbackKey, params object?[] args)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture)
                     ?? Resources.GetString(fallbackKey, CultureInfo.CurrentUICulture)
                     ?? key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    /// <summary><c>ar</c> or <c>en</c>.</summary>
    public static string NormalizeCulture(string? culture) =>
        culture is not null && culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

    /// <summary>The culture of the current request (<c>ar</c> / <c>en</c>).</summary>
    public static string CurrentCulture => NormalizeCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>Runs a block in <paramref name="culture"/> (<c>ar</c> / <c>en</c>); restores the previous culture on dispose.</summary>
    public static IDisposable UseCulture(string culture) => new CultureScope(culture);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string culture) =>
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(NormalizeCulture(culture));

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
