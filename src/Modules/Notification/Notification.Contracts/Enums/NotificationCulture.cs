using System.Text.Json.Serialization;

namespace Notification.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationCulture
{
    En = 0,
    Ar = 1,
}

public static class NotificationCultures
{
    /// <summary>
    /// The notification culture of the current request (<see cref="System.Globalization.CultureInfo.CurrentUICulture"/>,
    /// set by request localization from <c>Accept-Language</c>): Arabic for any <c>ar*</c> culture, else English.
    /// Use it for mail triggered by the user's own action (security alerts), so it arrives in the language they use.
    /// </summary>
    public static NotificationCulture FromCurrentUICulture() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase)
            ? NotificationCulture.Ar
            : NotificationCulture.En;
}
