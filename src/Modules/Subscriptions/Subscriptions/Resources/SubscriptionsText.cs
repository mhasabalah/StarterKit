using System.Globalization;
using System.Resources;

namespace Subscriptions.Resources;

/// <summary>
/// Looks a <c>SubscriptionsResource</c> string up in an EXPLICIT culture, for text stored in both languages at once
/// (in-app notifications are bilingual; the recipient culture picks one at delivery). Same resx files as the localizer.
/// </summary>
internal static class SubscriptionsText
{
    private static readonly ResourceManager Manager = new(typeof(SubscriptionsResource));
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");

    public static string En(string key, params object[] args) => Format(Manager.GetString(key, CultureInfo.InvariantCulture), key, args);

    public static string Ar(string key, params object[] args) => Format(Manager.GetString(key, Arabic), key, args);

    private static string Format(string? text, string key, object[] args) =>
        text is null ? key : args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
}
