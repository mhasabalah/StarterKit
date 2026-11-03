namespace Notification.Channels.Email;

/// <summary>
/// Builds a fully populated model dictionary for any <see cref="EmailTemplate"/> by instantiating its
/// model class (found through <see cref="EmailTemplateRegistry.GetModelType"/>), filling every public
/// settable property and calling the model's own <c>ToDictionary()</c>. New templates get sample data
/// automatically; used by the startup smoke-render, the admin test-email endpoint and the test suite.
/// </summary>
public sealed class EmailTemplateSamples(EmailTemplateRegistry registry, IOptions<EmailOptions> options)
{
    /// <param name="template">Template to build a model for.</param>
    /// <param name="valueFor">
    /// Optional override for string values: receives the property name, returns the value
    /// (or null to use the default sample value).
    /// </param>
    public Dictionary<string, object> Build(EmailTemplate template, Func<string, string?>? valueFor = null)
    {
        var modelType = registry.GetModelType(template)
                        ?? throw new InvalidOperationException($"No model type registered for EmailTemplate.{template}.");

        var model = Activator.CreateInstance(modelType)
                    ?? throw new InvalidOperationException($"Cannot instantiate {modelType.Name}.");

        foreach (var prop in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite))
        {
            if (prop.PropertyType == typeof(string))
                prop.SetValue(model, valueFor?.Invoke(prop.Name) ?? DefaultString(prop.Name));
            else if (prop.PropertyType == typeof(int))
                prop.SetValue(model, 7);
            else if (prop.PropertyType == typeof(decimal))
                prop.SetValue(model, 99.5m);
            else if (prop.PropertyType == typeof(bool))
                prop.SetValue(model, true);
            else if (prop.PropertyType == typeof(DateTime))
                prop.SetValue(model, DateTime.UtcNow.Date);
        }

        var toDictionary = modelType.GetMethod("ToDictionary", Type.EmptyTypes)
                           ?? throw new InvalidOperationException($"{modelType.Name} has no ToDictionary() method.");

        return (Dictionary<string, object>)toDictionary.Invoke(model, null)!;
    }

    private string DefaultString(string propertyName)
    {
        var opts = options.Value;
        if (propertyName.EndsWith("Url", StringComparison.Ordinal))
            return $"{opts.AppBaseUrl.TrimEnd('/')}/sample/{propertyName.ToLowerInvariant()}";

        if (propertyName.Contains("Email", StringComparison.Ordinal))
            return opts.SupportEmail;

        if (propertyName.Contains("Name", StringComparison.Ordinal))
            return "Sample " + propertyName;

        return "Sample " + propertyName;
    }
}
