using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Options;

/// <summary>
/// Server-side secrets of the Identity module (section <c>Identity</c>).
/// </summary>
public class IdentitySecurityOptions
{
    public const string SectionName = "Identity";

    /// <summary>Minimum length of <see cref="OtpHmacKey"/> outside Development.</summary>
    public const int MinOtpHmacKeyLength = 32;

    /// <summary>
    /// HMAC key used to hash one-time codes before they are stored (<c>Identity:OtpHmacKey</c>).
    /// Required (>= 32 chars) outside Development/Testing; there a random per-process key is used when it is missing.
    /// </summary>
    public string OtpHmacKey { get; set; } = string.Empty;

    /// <summary>Initial SuperAdmin account (<c>Identity:SeedAdmin</c>).</summary>
    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

public class SeedAdminOptions
{
    public string Email { get; set; } = "admin@starterkit.local";
    public string UserName { get; set; } = "superadmin";

    /// <summary>
    /// Password of the seeded SuperAdmin (<c>Identity:SeedAdmin:Password</c>, normally a Docker secret).
    /// Required outside Development/Testing when the SuperAdmin does not exist yet. Never logged.
    /// The account must change it on first login.
    /// </summary>
    public string? Password { get; set; }
}

internal static class IdentityHostEnvironment
{
    /// <summary>Development or the Testing environment used by the E2E test host.</summary>
    public static bool IsDevelopmentLike(this IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Testing");
}

/// <summary>Fails startup (ValidateOnStart) when the OTP key is missing or weak outside Development.</summary>
public sealed class IdentitySecurityOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<IdentitySecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentitySecurityOptions options)
    {
        if (environment.IsDevelopmentLike())
        {
            return ValidateOptionsResult.Success;
        }

        return options.OtpHmacKey.Length < IdentitySecurityOptions.MinOtpHmacKeyLength
            ? ValidateOptionsResult.Fail(
                $"Identity:OtpHmacKey must be set to a random secret of at least " +
                $"{IdentitySecurityOptions.MinOtpHmacKeyLength} characters outside Development.")
            : ValidateOptionsResult.Success;
    }
}
