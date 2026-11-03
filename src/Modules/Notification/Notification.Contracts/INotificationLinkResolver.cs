namespace Notification.Contracts;

/// <summary>
/// Builds links that are put into notifications. Relative URLs are resolved against the public app URL that
/// notifications link to (<c>Email:AppBaseUrl</c>); absolute http(s) URLs are returned unchanged.
/// </summary>
public interface INotificationLinkResolver
{
    string ToAbsolute(string url);
}
