namespace Shared.Email.Contracts;

public interface IEmailTemplateRenderer
{
    Task<string> RenderAsync(
        string templateName,
        Dictionary<string, object> model,
        EmailCulture culture,
        CancellationToken ct = default);
}
