namespace Notification.Options;

/// <summary>
/// Built once from <see cref="WhatsAppOptions"/>. Configuration completeness (every
/// <see cref="WhatsAppTemplate"/> has an entry under <c>WhatsApp:Templates</c> with a non-empty
/// MetaTemplateName) is enforced at startup by <see cref="WhatsAppOptionsValidator"/> (ValidateOnStart).
/// </summary>
public sealed class WhatsAppTemplateRegistry
{
    private readonly IReadOnlyDictionary<WhatsAppTemplate, WhatsAppTemplateConfig> _registry;

    public WhatsAppTemplateRegistry(IOptions<WhatsAppOptions> options)
        : this(options.Value)
    {
    }

    public WhatsAppTemplateRegistry(WhatsAppOptions options)
    {
        var errors = WhatsAppOptionsValidator.FindErrors(options);
        if (errors.Count > 0)
            throw new InvalidOperationException("WhatsAppTemplateRegistry: " + string.Join(" ", errors));

        _registry = Enum.GetValues<WhatsAppTemplate>()
            .ToDictionary(v => v, v => options.Templates[GetKey(v)]);
    }

    public WhatsAppTemplateConfig Resolve(WhatsAppTemplate template)
        => _registry[template];

    /// <summary>Reads the <see cref="WhatsAppKeyAttribute"/> value for the given enum member.</summary>
    public static string GetKey(WhatsAppTemplate template)
    {
        var attr = typeof(WhatsAppTemplate)
            .GetField(template.ToString())
            ?.GetCustomAttribute<WhatsAppKeyAttribute>();
        return attr?.Key ?? template.ToString();
    }
}

/// <summary>Startup validation for <see cref="WhatsAppOptions"/> (registered with ValidateOnStart).</summary>
public sealed class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        var errors = FindErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> FindErrors(WhatsAppOptions options)
    {
        var errors = new List<string>();
        foreach (var value in Enum.GetValues<WhatsAppTemplate>())
        {
            var key = WhatsAppTemplateRegistry.GetKey(value);
            if (!options.Templates.TryGetValue(key, out var config) || config is null)
            {
                errors.Add($"WhatsApp:Templates:{key} is missing (WhatsAppTemplate.{value}).");
                continue;
            }

            if (string.IsNullOrWhiteSpace(config.MetaTemplateName))
                errors.Add($"WhatsApp:Templates:{key}:MetaTemplateName must not be empty.");

            if (string.IsNullOrWhiteSpace(config.LanguageCode))
                errors.Add($"WhatsApp:Templates:{key}:LanguageCode must not be empty.");
        }

        return errors;
    }
}
