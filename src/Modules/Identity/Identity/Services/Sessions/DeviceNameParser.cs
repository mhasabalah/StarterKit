namespace Identity.Services.Sessions;

/// <summary>
/// Turns a raw <c>User-Agent</c> into a short label for the "your sessions" list ("Chrome on Windows").
/// Deliberately small: it only needs to be recognisable to the user, not exact.
/// </summary>
public static class DeviceNameParser
{
    public const int MaxUserAgentLength = 512;
    public const int MaxDeviceNameLength = 100;

    public static string? Truncate(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
            ? null
            : userAgent.Length <= MaxUserAgentLength ? userAgent : userAgent[..MaxUserAgentLength];

    public static string? Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var browser = Browser(userAgent);
        var os = OperatingSystem(userAgent);

        var name = (browser, os) switch
        {
            (null, null) => userAgent.Split('/', ' ')[0],
            (null, _) => os!,
            (_, null) => browser!,
            _ => $"{browser} on {os}"
        };

        return name.Length <= MaxDeviceNameLength ? name : name[..MaxDeviceNameLength];
    }

    // Order matters: Edge/Opera/Chrome UAs also contain "Chrome"/"Safari".
    private static string? Browser(string ua) =>
        Has(ua, "Edg/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/") ? "Edge"
        : Has(ua, "OPR/") || Has(ua, "Opera") ? "Opera"
        : Has(ua, "Firefox/") || Has(ua, "FxiOS/") ? "Firefox"
        : Has(ua, "SamsungBrowser/") ? "Samsung Internet"
        : Has(ua, "Chrome/") || Has(ua, "CriOS/") ? "Chrome"
        : Has(ua, "Safari/") ? "Safari"
        : Has(ua, "PostmanRuntime") ? "Postman"
        : Has(ua, "curl/") ? "curl"
        : Has(ua, "okhttp") ? "Android app"
        : Has(ua, "CFNetwork") ? "iOS app"
        : null;

    private static string? OperatingSystem(string ua) =>
        Has(ua, "Windows") ? "Windows"
        : Has(ua, "Android") ? "Android"
        : Has(ua, "iPhone") || Has(ua, "iPad") || Has(ua, "iPod") ? "iOS"
        : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS"
        : Has(ua, "CrOS") ? "ChromeOS"
        : Has(ua, "Linux") ? "Linux"
        : null;

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
