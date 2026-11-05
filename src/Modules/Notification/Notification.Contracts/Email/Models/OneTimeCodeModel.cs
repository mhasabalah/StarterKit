namespace Notification.Contracts.Email.Models;

/// <summary>A one-time code sent by email (email verification, or a sign-in code when email is the 2FA channel).</summary>
public sealed class OneTimeCodeModel
{
    public const string EmailVerification = "email_verification";
    public const string SignIn = "sign_in";

    public string Code { get; set; } = string.Empty;

    /// <summary><see cref="EmailVerification"/> or <see cref="SignIn"/> - selects the wording.</summary>
    public string Purpose { get; set; } = EmailVerification;

    public int ExpiresInMinutes { get; set; } = 10;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["Code"] = Code,
        ["Purpose"] = Purpose,
        ["ExpiresInMinutes"] = ExpiresInMinutes
    };
}
