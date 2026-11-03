namespace Identity.Options;

public class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    /// <summary>Base URL of the frontend app that hosts <c>/confirm-email</c>, e.g. https://app.example.com</summary>
    public string FrontendBaseUrl { get; init; } = string.Empty;

    /// <summary>Lifetime of a confirmation link. Independent of the (short) password-reset token lifetime.</summary>
    public int TokenExpiryHours { get; init; } = 24;

    /// <summary>Send the confirmation email automatically when an email account registers or adds an email.</summary>
    public bool SendOnRegister { get; init; } = true;
}
