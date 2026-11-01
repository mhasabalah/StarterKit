namespace Shared.Email.Models;

/// <summary>Pure data bag. Template identity and subjects live in nested <see cref="Metadata"/>.</summary>
public sealed class PasswordResetRequiredModel
{
    public string CompanyName { get; set; } = string.Empty;
    public string ResetUrl { get; set; } = string.Empty;
    public int ExpiresInHours { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["CompanyName"] = CompanyName,
        ["ResetUrl"] = ResetUrl,
        ["ExpiresInHours"] = ExpiresInHours
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.PasswordResetRequired;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "مطلوب إعادة تعيين كلمة المرور"
            : "Password Reset Required";
    }
}
