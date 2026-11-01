namespace Shared.Notification.Options;

/// <summary>
/// Built once at startup from <see cref="WhatsAppOptions"/>.
/// Validates that every <see cref="WhatsAppTemplate"/> enum value has a corresponding
/// entry in <c>WhatsApp:Templates</c> configuration — throws at boot if any are missing.
/// Replaces the old magic-string dictionary lookup in <c>WhatsAppChannelAdapter</c>.
/// </summary>
public sealed class WhatsAppTemplateRegistry
{
    private readonly IReadOnlyDictionary<WhatsAppTemplate, WhatsAppTemplateConfig> _registry;

    public WhatsAppTemplateRegistry(WhatsAppOptions options)
    {
        var dict = new Dictionary<WhatsAppTemplate, WhatsAppTemplateConfig>();

        foreach (var value in Enum.GetValues<WhatsAppTemplate>())
        {
            var key = GetKey(value);
            if (!options.Templates.TryGetValue(key, out var config))
                throw new InvalidOperationException(
                    $"WhatsAppTemplateRegistry: no configuration found for WhatsAppTemplate.{value} " +
                    $"(expected key \"{key}\" under WhatsApp:Templates). " +
                    $"Add the entry in appsettings or secrets.");
            dict[value] = config;
        }

        _registry = dict;
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
