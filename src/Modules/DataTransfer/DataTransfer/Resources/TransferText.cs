using System.Globalization;
using System.Resources;

namespace DataTransfer.Resources;

/// <summary>
/// Localized engine text WITHOUT dependency injection (exceptions, the job runner, the readers), from the same
/// DataTransferResource.resx / .ar.resx. Uses <see cref="CultureInfo.CurrentUICulture"/>: the request culture in a
/// handler, the requester's culture inside a job (the runner sets it for the whole run).
/// </summary>
public static class TransferText
{
    private static readonly ResourceManager Resources =
        new("DataTransfer.Resources.DataTransferResource", typeof(TransferText).Assembly);

    public static string Get(string key, params object?[] args)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    /// <summary>The text in an explicit culture (<c>ar</c> / <c>en</c>), e.g. for a file written for someone else.</summary>
    public static string For(string culture, string key, params object?[] args)
    {
        var target = CultureInfo.GetCultureInfo(TransferCulture.Normalize(culture));
        var format = Resources.GetString(key, target) ?? key;
        return args.Length == 0 ? format : string.Format(target, format, args);
    }

    /// <summary>Runs a block in <paramref name="culture"/> (<c>ar</c> / <c>en</c>); restores the previous culture on dispose.</summary>
    public static IDisposable UseCulture(string culture) => new CultureScope(culture);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string culture) =>
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(TransferCulture.Normalize(culture));

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
