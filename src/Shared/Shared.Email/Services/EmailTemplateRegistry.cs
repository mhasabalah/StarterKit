namespace Shared.Email.Services;

/// <summary>
/// Thin aggregator: built once at startup from all IEmailTemplate registrations.
/// No switch, no hardcoded strings — each model's Metadata class registers itself via DI (Scrutor scan).
/// Adding a new template = add one model file. Nothing else changes.
/// </summary>
public sealed class EmailTemplateRegistry
{
    private readonly IReadOnlyDictionary<EmailTemplate, IEmailTemplate> _registry;

    public EmailTemplateRegistry(IEnumerable<IEmailTemplate> templates)
    {
        _registry = templates.ToDictionary(t => t.Template);

        // CQ3: Validate that every EmailTemplate enum value has a registered IEmailTemplate.
        // A missing registration means a model's Metadata class was not wired up via Scrutor.
        var expected = Enum.GetValues<EmailTemplate>().Length;
        if (_registry.Count != expected)
        {
            var missing = Enum.GetValues<EmailTemplate>()
                .Where(v => !_registry.ContainsKey(v))
                .Select(v => v.ToString());
            throw new InvalidOperationException(
                $"EmailTemplateRegistry is incomplete. Missing IEmailTemplate registrations for: " +
                string.Join(", ", missing) +
                ". Ensure each model's nested Metadata class is in the Shared.Email assembly.");
        }
    }

    public string ResolveSubject(EmailTemplate template, EmailCulture culture)
        => _registry.TryGetValue(template, out var t)
            ? t.GetSubject(culture)
            : template.ToString();
}
