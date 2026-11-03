using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Identity.Services.EmailConfirmation;

/// <summary>
/// Own provider (own data-protection purpose and lifetime) for email-confirmation tokens, so a confirmation link can
/// live for a day while password-reset tokens stay short (<c>PasswordReset:TokenExpiryHours</c>).
/// </summary>
public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "EmailConfirmation";

    public EmailConfirmationTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromHours(24);
    }
}

public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);
