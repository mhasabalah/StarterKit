using Shared.Email.Options;

namespace Shared.Email.Models;

/// <summary>Pure data bag. Template identity and subjects live in nested <see cref="Metadata"/>.</summary>
public sealed class WelcomeUserModel
{
    public string FirstName { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["firstName"] = FirstName
    };

    public sealed class Metadata(IOptions<EmailOptions> options) : IEmailTemplate
    {
        private readonly string _brandName = options.Value.BrandName;

        public EmailTemplate Template => EmailTemplate.WelcomeUser;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? $"مرحباً بك في {_brandName}"
            : $"Welcome to {_brandName}";
    }
}
