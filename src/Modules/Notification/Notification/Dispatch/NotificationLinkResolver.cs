namespace Notification.Dispatch;

/// <summary><see cref="INotificationLinkResolver"/> over <c>Email:AppBaseUrl</c>.</summary>
internal sealed class NotificationLinkResolver(IOptions<EmailOptions> options) : INotificationLinkResolver
{
    public string ToAbsolute(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return url;

        return options.Value.AppBaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
    }
}
