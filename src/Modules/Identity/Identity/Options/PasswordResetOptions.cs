namespace Identity.Options;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Base URL of the frontend app, e.g. https://app.example.com</summary>
    public string FrontendBaseUrl { get; init; } = string.Empty;

    public int TokenExpiryHours { get; init; } = 1;
    public int OtpExpiryMinutes { get; init; } = 15;
    public int MaxOtpAttempts { get; init; } = 3;
}
