namespace Notification.Contracts.Push;

/// <summary>
/// A mobile / web push notification, delivered through FCM to every active device of the user.
/// </summary>
/// <param name="Title">Headline shown by the OS (bilingual; the recipient's culture picks one).</param>
/// <param name="Body">Body shown by the OS.</param>
/// <param name="Data">Optional data payload for the app (ids, deep-link route). Max <see cref="MaxDataEntries"/> entries; no PII.</param>
/// <param name="Sensitive">
/// True when the text must not leave the platform: push content transits Google/Apple, so the OS shows a generic
/// "You have a new notification" and the app fetches the details from the API.
/// </param>
/// <param name="TimeToLive">How long FCM keeps an undelivered message (device offline). Default: <c>Push:DefaultTimeToLive</c>.</param>
public sealed record PushContent(
    LocalizedText Title,
    LocalizedText Body,
    IReadOnlyDictionary<string, string>? Data = null,
    bool Sensitive = false,
    TimeSpan? TimeToLive = null)
{
    public const int MaxDataEntries = 20;
}
