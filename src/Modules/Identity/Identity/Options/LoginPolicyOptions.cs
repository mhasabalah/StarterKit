namespace Identity.Options;

/// <summary>
/// Authentication policy configuration for login behavior
/// </summary>
public class LoginPolicyOptions
{
    public const string SectionName = "LoginPolicy";

    /// <summary>
    /// Require phone verification before generating tokens
    /// Default: false (allow login without phone verification)
    /// </summary>
    public bool RequirePhoneVerification { get; init; }

    /// <summary>
    /// Require email verification before generating tokens
    /// Default: false (allow login without email verification)
    /// </summary>
    public bool RequireEmailVerification { get; init; }

    /// <summary>
    /// Require profile completion before generating tokens
    /// Default: false (allow login with incomplete profile)
    /// </summary>
    public bool RequireProfileCompletion { get; init; }

    /// <summary>
    /// Allow partial login (return user info without tokens)
    /// when verification requirements are not met
    /// Default: false (always return tokens)
    /// </summary>
    public bool AllowPartialLogin { get; init; }
}
