namespace Notification.Channels.Email;

/// <summary>
/// Thin aggregator: built once at startup from all IEmailTemplate registrations.
/// No switch, no hardcoded strings - each template's Subjects class registers itself via DI (assembly scan).
/// Adding a template = the enum value + a contracts model + a Subjects class + the .liquid file (startup validates all four).
/// </summary>
public sealed class EmailTemplateRegistry
{
    private readonly IReadOnlyDictionary<EmailTemplate, IEmailTemplate> _registry;

    public EmailTemplateRegistry(IEnumerable<IEmailTemplate> templates)
    {
        _registry = templates.ToDictionary(t => t.Template);

        // CQ3: Validate that every EmailTemplate enum value has a registered IEmailTemplate.
        // A missing registration means a Channels/Email/Subjects class is missing.
        var expected = Enum.GetValues<EmailTemplate>().Length;
        if (_registry.Count != expected)
        {
            var missing = Enum.GetValues<EmailTemplate>()
                .Where(v => !_registry.ContainsKey(v))
                .Select(v => v.ToString());
            throw new InvalidOperationException(
                $"EmailTemplateRegistry is incomplete. Missing IEmailTemplate registrations for: " +
                string.Join(", ", missing) +
                ". Add an IEmailTemplate implementation under Channels/Email/Subjects.");
        }
    }

    /// <summary>Localized subject, sanitized for the Subject header (no CR/LF / control characters).</summary>
    public string ResolveSubject(EmailTemplate template, NotificationCulture culture)
        => EmailContentSafety.SanitizeSubject(_registry.TryGetValue(template, out var t)
            ? t.GetSubject(culture)
            : template.ToString());

    /// <summary>
    /// The contracts model class of <paramref name="template"/>. Used to build sample data for smoke renders and the
    /// test endpoint.
    /// </summary>
    public Type? GetModelType(EmailTemplate template)
        => _registry.TryGetValue(template, out var t) ? t.ModelType : null;
}
