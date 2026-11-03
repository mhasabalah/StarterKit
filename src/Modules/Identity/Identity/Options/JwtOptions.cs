using Microsoft.Extensions.Options;

namespace Identity.Options;

/// <summary>
/// JWT settings. Issuer, audience, lifetime and signing-key validation are always on: there are deliberately
/// no switches to turn them off.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "JwtSettings";

    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinSecretKeyBytes = 32;

    public required string SecretKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenExpirationMinutes { get; init; } = 60;
    public int RefreshTokenExpirationDays { get; init; } = 7;
    public bool RequireHttpsMetadata { get; init; } = true;
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(options.SecretKey) ||
            Encoding.UTF8.GetByteCount(options.SecretKey) < JwtOptions.MinSecretKeyBytes)
        {
            errors.Add($"JwtSettings:SecretKey must be at least {JwtOptions.MinSecretKeyBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            errors.Add("JwtSettings:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            errors.Add("JwtSettings:Audience is required.");
        }

        if (options.AccessTokenExpirationMinutes <= 0)
        {
            errors.Add("JwtSettings:AccessTokenExpirationMinutes must be positive.");
        }

        if (options.RefreshTokenExpirationDays <= 0)
        {
            errors.Add("JwtSettings:RefreshTokenExpirationDays must be positive.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
