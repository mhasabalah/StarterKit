namespace Notification.Channels.Email;

/// <summary>
/// One per <see cref="EmailTemplate"/> (see <c>Channels/Email/Subjects</c>): the template identity, its localized
/// subjects and the contracts model that carries its data. Discovered by assembly scan; the registry fails startup
/// when an <see cref="EmailTemplate"/> value has none.
/// </summary>
public interface IEmailTemplate
{
    EmailTemplate Template { get; }

    /// <summary>The <c>Notification.Contracts.Email.Models</c> class whose <c>ToDictionary()</c> feeds the template.</summary>
    Type ModelType { get; }

    string GetSubject(NotificationCulture culture);
}
