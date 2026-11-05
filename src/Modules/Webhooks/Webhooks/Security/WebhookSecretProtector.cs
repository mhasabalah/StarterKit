using Microsoft.AspNetCore.DataProtection;

namespace Webhooks.Security;

/// <summary>Encrypts endpoint signing secrets at rest with ASP.NET Core Data Protection.</summary>
public interface IWebhookSecretProtector
{
    /// <summary>A fresh <c>whsec_...</c> secret (plain text: show it to the user ONCE).</summary>
    string GenerateSecret();

    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public sealed class WebhookSecretProtector(IDataProtectionProvider provider) : IWebhookSecretProtector
{
    public const string Purpose = "Webhooks.EndpointSecret";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string GenerateSecret() => WebhookSigner.GenerateSecret();

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
