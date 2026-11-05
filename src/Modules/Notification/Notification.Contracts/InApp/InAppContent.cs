namespace Notification.Contracts.InApp;

/// <summary>
/// An in-app (inbox) notification. Stored per user and pushed in real time to the user's open sessions.
/// Text is bilingual; the recipient's culture picks the language at delivery time.
/// </summary>
/// <param name="Title">Short headline (max <see cref="TitleMaxLength"/> characters per language).</param>
/// <param name="Body">Body text (max <see cref="BodyMaxLength"/> characters per language). Plain text, never HTML.</param>
/// <param name="Category">
/// Machine-readable kind (e.g. <c>subscriptions.renewed</c>) clients group or route by. Max
/// <see cref="CategoryMaxLength"/> characters.
/// </param>
/// <param name="ActionUrl">
/// Optional deep link opened on click. Relative (<c>/billing/invoices/42</c>) or absolute on an allowed host
/// (<c>Email:AppBaseUrl</c> / <c>Email:AllowedLinkHosts</c>); anything else is dropped at delivery.
/// </param>
/// <param name="Severity">Presentation hint.</param>
/// <param name="Data">Optional small key/value payload for the client (ids, not PII). Max <see cref="MaxDataEntries"/> entries.</param>
public sealed record InAppContent(
    LocalizedText Title,
    LocalizedText Body,
    string Category,
    string? ActionUrl = null,
    NotificationSeverity Severity = NotificationSeverity.Info,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 2000;
    public const int CategoryMaxLength = 100;
    public const int ActionUrlMaxLength = 2000;
    public const int MaxDataEntries = 20;
}
