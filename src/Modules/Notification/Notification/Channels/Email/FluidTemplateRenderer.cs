using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Text;

namespace Notification.Channels.Email;

public sealed class FluidTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private readonly Dictionary<string, IFluidTemplate> _templates;
    private readonly TemplateOptions _options;
    private readonly EmailOptions _emailOptions;

    private const string BaseLayoutKey = "Layouts/BaseLayout";

    public FluidTemplateRenderer(IOptions<EmailOptions> emailOptions)
    {
        _emailOptions = emailOptions.Value;
        _templates = new Dictionary<string, IFluidTemplate>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(FluidTemplateRenderer).Assembly;

        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException($"Template resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();
            var key = ExtractTemplateKey(resourceName);

            if (!Parser.TryParse(source, out var template, out var error))
                throw new InvalidOperationException($"Failed to parse email template '{key}': {error}");

            sources[key] = source;
            _templates[key] = template;
        }

        if (!_templates.ContainsKey(BaseLayoutKey))
            throw new InvalidOperationException(
                $"Base layout template '{BaseLayoutKey}' is not registered. " +
                $"Add 'BaseLayout.liquid' under Templates/Layouts/.");

        _options = new TemplateOptions
        {
            FileProvider = new EmbeddedLiquidFileProvider(sources)
        };

        // C6: every href must go through `| safe_url` (http/https + allow-listed host only).
        _options.Filters.AddFilter("safe_url", (input, _, _) =>
            new ValueTask<FluidValue>(new StringValue(
                EmailContentSafety.SanitizeUrl(input.ToStringValue(), _emailOptions))));

        // CQ2: Every EmailTemplate enum value must have a matching .liquid file.
        foreach (var value in Enum.GetValues<EmailTemplate>())
        {
            var fileName = GetTemplateFileName(value);
            if (!_templates.ContainsKey(fileName))
                throw new InvalidOperationException(
                    $"No .liquid file found for EmailTemplate.{value}. " +
                    $"Add '{fileName}.liquid' as an EmbeddedResource under Templates/.");
        }
    }

    public async Task<string> RenderAsync(
        string templateName,
        Dictionary<string, object> model,
        NotificationCulture culture,
        CancellationToken ct = default)
    {
        if (!_templates.TryGetValue(templateName, out var template))
            throw new ArgumentException($"Email template '{templateName}' is not registered.", nameof(templateName));

        var cultureValue = culture == NotificationCulture.Ar ? "ar" : "en";

        var bodyContext = new TemplateContext(_options);
        SetGlobals(bodyContext, cultureValue);
        foreach (var (key, value) in model)
            bodyContext.SetValue(key, value);

        // C6: HTML-encode every {{ output }}. Model values (user names, rejection reasons, ...) are
        // untrusted, so they must never reach the mail body as markup. Only renderer-produced HTML
        // (the rendered body in the layout, captured component blocks) is emitted with `| raw`.
        var bodyContent = await template.RenderAsync(bodyContext, Encoder);

        var layoutContext = new TemplateContext(_options);
        SetGlobals(layoutContext, cultureValue);
        layoutContext.SetValue("content", bodyContent);
        return await _templates[BaseLayoutKey].RenderAsync(layoutContext, Encoder);
    }

    /// <summary>
    /// HTML encoder that escapes markup-significant characters (&lt; &gt; &amp; " ') but leaves
    /// non-Latin letters intact, so Arabic content is not turned into numeric entities.
    /// </summary>
    internal static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Values available to every template and the layout: culture, brandName, appBaseUrl,
    /// supportEmail, currentYear. Model values with the same key override these in the body.
    /// </summary>
    private void SetGlobals(TemplateContext context, string cultureValue)
    {
        context.SetValue("culture", cultureValue);
        context.SetValue("brandName", _emailOptions.BrandName);
        context.SetValue("appBaseUrl", _emailOptions.AppBaseUrl);
        context.SetValue("supportEmail", _emailOptions.SupportEmail);
        context.SetValue("currentYear", DateTime.UtcNow.Year);
    }

    /// <summary>
    /// Resolves an <see cref="EmailTemplate"/> enum value to its Liquid template file name
    /// via <see cref="TemplateFileAttribute"/>, falling back to the enum member name.
    /// </summary>
    public static string GetTemplateFileName(EmailTemplate template)
    {
        var attr = typeof(EmailTemplate)
            .GetField(template.ToString())
            ?.GetCustomAttribute<TemplateFileAttribute>();
        return attr?.FileName ?? template.ToString();
    }

    /// <summary>
    /// Converts a manifest resource name to a slash-separated template key.
    /// e.g. "Notification.Channels.Email.Templates.Layouts.BaseLayout.liquid" → "Layouts/BaseLayout"
    ///      "Notification.Channels.Email.Templates.WelcomeUser.liquid" → "WelcomeUser"
    /// </summary>
    private static string ExtractTemplateKey(string resourceName)
    {
        const string marker = ".Templates.";
        var index = resourceName.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return resourceName;

        var nameWithExtension = resourceName[(index + marker.Length)..];
        var key = nameWithExtension.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
            ? nameWithExtension[..^7]
            : nameWithExtension;

        // MSBuild uses dots as path separators in manifest names: "Layouts.BaseLayout" → "Layouts/BaseLayout"
        return key.Replace('.', '/');
    }

    // ── File provider for {% include 'Components/Button' %} resolution ──────────

    private sealed class EmbeddedLiquidFileProvider : IFileProvider
    {
        private readonly IReadOnlyDictionary<string, string> _sources;

        public EmbeddedLiquidFileProvider(IReadOnlyDictionary<string, string> sources)
            => _sources = sources;

        public IFileInfo GetFileInfo(string subpath)
        {
            var normalized = subpath.TrimStart('/').Replace('\\', '/');

            // Strip .liquid extension if present to produce the dictionary key
            var key = normalized.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                ? normalized[..^7]
                : normalized;

            return _sources.TryGetValue(key, out var content)
                ? new StringFileInfo(content, Path.GetFileName(key) + ".liquid")
                : new NotFoundFileInfo(subpath);
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
            => new NotFoundDirectoryContents();

        public IChangeToken Watch(string filter)
            => NullChangeToken.Singleton;
    }

    private sealed class StringFileInfo : IFileInfo
    {
        private readonly string _content;

        public StringFileInfo(string content, string name)
        {
            _content = content;
            Name = name;
        }

        public bool Exists       => true;
        public bool IsDirectory  => false;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Length       => Encoding.UTF8.GetByteCount(_content);
        public string Name       { get; }
        public string? PhysicalPath => null;

        public Stream CreateReadStream()
            => new MemoryStream(Encoding.UTF8.GetBytes(_content));
    }
}
