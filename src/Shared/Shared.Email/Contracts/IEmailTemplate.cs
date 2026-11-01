namespace Shared.Email.Contracts;

/// <summary>
/// Implemented by every email template model.
/// The model owns its template identity and its localized subjects —
/// no central registry of strings, no magic strings anywhere.
/// </summary>
public interface IEmailTemplate
{
    EmailTemplate Template { get; }
    string GetSubject(EmailCulture culture);
}
